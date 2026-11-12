using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SurgicalFoundations.Contracts;
using UnityEngine;

namespace SurgicalFoundations.Backend
{
    /// <summary>One upload waiting to happen, stored as its own file so a crash or power-off never loses it.</summary>
    [Serializable]
    public class SyncJob
    {
        public long seq;
        public string kind;
        /// <summary>Only this user's tokens may upload it (a shared headset can hold several learners' results).</summary>
        public string ownerUserId;
        public string sessionId;
        /// <summary>The request body, already serialized.</summary>
        public string body;
        /// <summary>Replay jobs: the local file to upload.</summary>
        public string filePath;
        public int attempts;
        public long notBeforeUnixMs;
        public string lastError;
    }

    /// <summary>
    /// Offline-first upload queue (FR-25, NFR-09). Jobs are persisted to disk, sent strictly in order per user, retried
    /// with backoff while offline or when the server is busy, and dropped only when the server says they can never
    /// succeed. Every request is idempotent on the server (client-generated ids), so re-sending after a lost
    /// response is always safe.
    /// </summary>
    public class SyncQueue
    {
        public static class Kinds
        {
            public const string CreateSession = "session.create";
            public const string Events = "session.events";
            public const string CompleteSession = "session.complete";
            public const string Replay = "replay.upload";
            public const string LessonAttempt = "lesson.attempt";
            public const string Enroll = "course.enroll";
        }

        // One small request a minute while offline is cheap; a longer wait would delay results reaching the LMS once back online.
        const int MaxBackoffSeconds = 60;

        readonly ApiClient api;
        readonly AuthService auth;
        readonly string directory;
        readonly List<SyncJob> jobs = new List<SyncJob>();
        long nextSeq;

        public SyncQueue(ApiClient api, AuthService auth, string directory)
        {
            this.api = api;
            this.auth = auth;
            this.directory = directory;
            Directory.CreateDirectory(directory);
            Load();
        }

        public event Action Changed;

        public int PendingCount => jobs.Count;
        public int PendingFor(string userId) => jobs.Count(j => j.ownerUserId == userId);
        public bool Uploading { get; private set; }
        public DateTime? LastSyncUtc { get; private set; }
        public string LastError { get; private set; }
        public static bool NetworkReachable => Application.internetReachability != NetworkReachability.NotReachable;
        /// <summary>No network, or the last attempt couldn't reach the server (Wi-Fi without internet, server down).</summary>
        public bool Offline => !NetworkReachable || serverUnreachable;
        bool serverUnreachable;

        public void Enqueue(string kind, string ownerUserId, string sessionId, object body, string filePath = null)
        {
            var job = new SyncJob
            {
                seq = nextSeq++,
                kind = kind,
                ownerUserId = ownerUserId,
                sessionId = sessionId,
                body = body == null ? null : ApiJson.Serialize(body),
                filePath = filePath
            };
            Write(job);
            jobs.Add(job);
            Changed?.Invoke();
        }

        /// <summary>Runs until cancelled (the Bootstrap scene's lifetime).</summary>
        public async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var worked = false;
                try { worked = await TryOneAsync(ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { Debug.LogException(e); }
                await Task.Delay(worked ? 50 : 2000, ct);
            }
        }

        async Task<bool> TryOneAsync(CancellationToken ct)
        {
            // Strict order per user: a session must exist before its events, events before completion.
            var job = jobs.FirstOrDefault(j => j.ownerUserId == auth.UserId);
            if (job == null || !NetworkReachable) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < job.notBeforeUnixMs) return false;

            var token = await auth.GetAccessTokenAsync(ct);
            if (token == null) return false;

            Uploading = true;
            Changed?.Invoke();
            ApiResponse result;
            try { result = await ExecuteAsync(job, token, ct); }
            finally { Uploading = false; }

            switch (result.Outcome)
            {
                case ApiOutcome.Ok:
                    serverUnreachable = false;
                    Remove(job);
                    LastSyncUtc = DateTime.UtcNow;
                    LastError = null;
                    break;
                case ApiOutcome.Rejected:
                    // The server will never accept this (e.g. session id taken by someone else). Keep the queue moving.
                    Debug.LogWarning($"[SyncQueue] Dropping {job.kind} for session {job.sessionId}: {result}");
                    Remove(job);
                    // A replay the server will never take would otherwise sit on the headset forever.
                    if (job.filePath != null) try { File.Delete(job.filePath); } catch (IOException) { }
                    break;
                case ApiOutcome.Unauthorized:
                    auth.InvalidateAccessToken();
                    break;
                default:
                    serverUnreachable = result.Outcome == ApiOutcome.Offline;
                    job.attempts++;
                    job.lastError = result.Error;
                    job.notBeforeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                                          1000L * Math.Min(MaxBackoffSeconds, 1 << Math.Min(job.attempts, 9));
                    LastError = result.Outcome == ApiOutcome.Offline ? "offline" : result.Error;
                    Write(job);
                    break;
            }
            Changed?.Invoke();
            return result.Ok;
        }

        async Task<ApiResponse> ExecuteAsync(SyncJob job, string token, CancellationToken ct)
        {
            switch (job.kind)
            {
                case Kinds.CreateSession:
                    return await api.SendJsonAsync("POST", ApiRoutes.Sessions, job.body, token, ct);
                case Kinds.Events:
                    return await api.SendJsonAsync("POST", ApiRoutes.SessionEvents(job.sessionId), job.body, token, ct);
                case Kinds.CompleteSession:
                    return await api.SendJsonAsync("POST", ApiRoutes.CompleteSession(job.sessionId), job.body, token, ct);
                case Kinds.Replay:
                    return await UploadReplayAsync(job, token, ct);
                case Kinds.LessonAttempt:
                    return await api.SendJsonAsync("POST", ApiRoutes.LessonAttempts, job.body, token, ct);
                case Kinds.Enroll:
                    return await api.SendJsonAsync("PUT", ApiRoutes.Enroll(job.sessionId), job.body ?? "{}", token, ct);
                default:
                    return new ApiResponse(ApiOutcome.Rejected, 0, null, "Unknown job kind " + job.kind);
            }
        }

        async Task<ApiResponse> UploadReplayAsync(SyncJob job, string token, CancellationToken ct)
        {
            if (!File.Exists(job.filePath)) return new ApiResponse(ApiOutcome.Rejected, 0, null, "Replay file is gone.");

            // Step 1: register (idempotent by replay id) and get a pre-signed URL.
            var created = await api.SendJsonAsync("POST", ApiRoutes.Replays, job.body, token, ct);
            if (!created.Ok) return created;
            var slot = created.Read<CreateReplayResponse>();

            // Step 2: the file goes straight to object storage, never through the API (NFR-16).
            if (slot.uploadRequired)
            {
                var request = ApiJson.Deserialize<CreateReplayRequest>(job.body);
                var put = await api.PutFileAsync(slot.uploadUrl, job.filePath, request.contentType, ct);
                if (!put.Ok) return put.Outcome == ApiOutcome.Rejected ? new ApiResponse(ApiOutcome.Retry, put.Status, put.Body, "Upload URL expired") : put;
            }

            // Step 3: reading it back makes the server confirm the file landed.
            var check = await api.GetAsync(ApiRoutes.Replay(slot.replayId), token, ct);
            if (check.Ok && check.Read<ReplayInfo>()?.status == ReplayStatus.Uploaded)
            {
                try { File.Delete(job.filePath); } catch (IOException) { }
                return check;
            }
            return new ApiResponse(ApiOutcome.Retry, check.Status, check.Body, "Replay not confirmed yet");
        }

        void Load()
        {
            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    var job = ApiJson.Deserialize<SyncJob>(File.ReadAllText(file));
                    if (job != null) jobs.Add(job);
                }
                catch (Exception e) { Debug.LogWarning($"[SyncQueue] Skipping unreadable job {file}: {e.Message}"); }
            }
            jobs.Sort((a, b) => a.seq.CompareTo(b.seq));
            nextSeq = jobs.Count == 0 ? 1 : jobs[jobs.Count - 1].seq + 1;
        }

        string PathFor(SyncJob job) => Path.Combine(directory, job.seq.ToString("D12") + ".json");

        /// <summary>Write-then-rename, so a power cut mid-write leaves the previous version, never a half file.</summary>
        void Write(SyncJob job)
        {
            var path = PathFor(job);
            var temp = path + ".tmp";
            File.WriteAllText(temp, ApiJson.Serialize(job));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        void Remove(SyncJob job)
        {
            jobs.Remove(job);
            try { File.Delete(PathFor(job)); } catch (IOException) { }
        }
    }
}
