<div align="center">

# Surgical Foundations VR

### Practise the first steps of keyhole surgery before you ever touch a patient.

A virtual-reality operating theatre for **Meta Quest 3** where medical students and junior surgical trainees
scrub in, prepare a sterile field, place their ports and operate through a laparoscope,
with a guide at their shoulder and honest feedback on every step.

![Platform](https://img.shields.io/badge/Platform-Meta%20Quest%203-0B1517)
![Session](https://img.shields.io/badge/Session-about%2015%20minutes-5CE0C8)
![Status](https://img.shields.io/badge/Status-Playable%20prototype-F5B54B)

![A learner's view during port placement: trocar guidance, the laparoscope feed on the tower monitor, and the draped patient](Docs/Images/access-stage.png)

<sub>Placing a port: live guidance on angle, depth and force, the laparoscope's view on the tower monitor, and the patient draped for surgery.</sub>

</div>

---

## Why this matters

**Keyhole (laparoscopic) surgery is one of the hardest skills a young surgeon learns.** The surgeon looks at a flat screen instead of into the body, works through long instruments that pivot at the skin, so the tip moves the *opposite* way to the hand, and feels far less of the tissue than with open surgery. Those skills take many hours of deliberate practice to build.

**Much of that practice still happens in the real operating theatre**, where time is scarce, the pressure is high and every mistake matters to a patient. Some of the most important lessons aren't about the instruments at all: a glove brushing a non-sterile edge, a forgotten swab, a port placed at the wrong angle. They are easy to make and costly to learn the hard way.

**Surgical Foundations VR gives learners somewhere safe to get those basics right first.** In the headset they can:

- **Repeat** a full procedure as often as they need, at any hour, without booking a theatre, a mentor or a simulator lab.
- **Build habits** of sterile technique and counting that protect patients, with a mistake shown the moment it happens.
- **Train the awkward hand-eye skills** of keyhole surgery: working through ports while watching a screen.
- **See their progress** in clear, consistent feedback, rather than depending on who happened to be supervising.

It doesn't replace supervised training in the real theatre. It helps learners arrive there better prepared, more confident and safer.

---

## Who it's for

| | |
|---|---|
| **Medical students** | A first, low-stress encounter with the theatre: how to scrub, gown, keep things sterile and what a laparoscopic operation looks like from the surgeon's position |
| **Junior surgical trainees** | Repeatable practice of port placement, instrument handling through the scope, and the counting and closing routine |
| **Educators** | A standard scenario every learner goes through the same way, with the same criteria, making it fairer to compare and easier to see where a group needs help |
| **Training programmes** | Practice on a standalone headset, designed to work without a network and to report results to the learning platform the institution already uses |

---

## The experience

One learner, one operating theatre, about fifteen minutes from sign-in to results.

```mermaid
flowchart LR
    A([Sign in]) --> B[Lobby]
    B --> C[Skills lab<br/>set-up and practice]
    C --> D[1 · Prep]
    B --> D
    D --> E[2 · Access]
    E --> F[3 · Operate]
    F --> G[4 · Close]
    G --> H([Results])
    H -.->|try again| D
```

| Step | What the learner does |
|---|---|
| **Sign in & lobby** | Signs in with a code from their phone, then chooses how to train: **Guided** (hints, voice prompts, live feedback) or **Assessment** (no help, just a result), standing or seated, controllers or bare hands. |
| **Skills lab** | Sets their height and the table height so the theatre fits their body, then practises the controls on a peg board. |
| **1 · Prep** | Scrubs at the sink while a demo shows the correct hand movements, gowns and gloves, checks the sterile back table, counts the instruments and drapes the patient. |
| **2 · Access** | Marks where the ports go on the abdomen and inserts them one by one, watching the entry angle, depth and force. |
| **3 · Operate** | Works through the laparoscope with a real camera view on the monitor: moving objects, dissecting and clipping. The guide calls out when an instrument drifts out of view. |
| **4 · Close** | Counts every instrument and swab (one is hidden on purpose) and removes the ports safely, camera port last. |
| **Results** | An overall score, a score per stage and the three things to work on next, with the option to repeat a stage or the whole scenario. |

A pause menu is always one button away, subtitles accompany every voice prompt, and every move between areas is a gentle fade, never a camera movement that could cause motion sickness.

### Feedback that teaches

Everything the learner does is checked against the surgical protocol and falls into one of three kinds:

- 🟢 **On protocol:** done correctly.
- 🟠 **Delayed:** right action, too late (for example, correcting a drifting instrument only after a prompt).
- 🔴 **Deviation:** something that would put a patient at risk (for example, a gloved hand touching a non-sterile surface). The edges of the view flash, a sound plays and the learner is told what went wrong and why.

In **Guided** mode this feedback appears in the moment, like a mentor at your shoulder. In **Assessment** mode it stays silent until the results, so the score reflects what the learner can do on their own.

---

## A look inside

| The operating theatre | The skills lab and ward |
|---|---|
| ![The operating theatre with the table, surgical lights, laparoscopic tower, anaesthesia area and equipment](Docs/Images/theatre-overview.png) | ![The skills lab with the adjustable training table and a ward bay with a hospital bed, IV stand and oxygen](Docs/Images/skills-lab.png) |
| **Scrubbing in, with the correct technique demonstrated** | **The calm lobby where every session starts** |
| ![The scrub sink with running water, demonstration hands and the scrub checklist](Docs/Images/prep-scrub.png) | ![The lobby with the sign-in panel and a waiting area](Docs/Images/lobby.png) |

![The characters, patient and hospital equipment that populate the simulation](Docs/Images/imported-models.png)

<sub>The people and equipment of the simulation: scrub nurse, anaesthetist, the patient on the operating table, scrub sink, hospital bed, IV stand, oxygen, heart-lung machine, anaesthesia cart, monitors and furniture.</sub>

**More angles:** every area, stage, lab and piece of equipment from several sides in the **[Gallery](Docs/Gallery.md)**.

| | | |
|---|---|---|
| ![Theatre from the head end](Docs/Images/Gallery/Theatre/theatre_2_head_end.png) | ![Course browser](Docs/Images/Gallery/Lobby/lobby_2_course_browser.png) | ![A procedure lab](Docs/Images/Gallery/ProcedureLab/procedure_lab_2_panel.png) |
| ![Theatre from above](Docs/Images/Gallery/Theatre/theatre_5_above.png) | ![Operating through the ports](Docs/Images/Gallery/Stages/stage_3_operate_tower.png) | ![Anaesthesia workstation, four angles](Docs/Images/Gallery/Equipment/anesthesia_machine_a01.png) |

---

## What makes it different

- **A complete procedure, not a single drill.** Learners go from the scrub sink to closing, in the order it happens in real life, so habits connect into a routine.
- **A real laparoscope view.** A camera inside the patient's abdomen feeds the tower monitor, so learners practise watching the screen instead of their hands.
- **Designed around patient safety.** Sterility, counting and safe port placement are treated as seriously as the surgical skill itself.
- **Fair and consistent.** Every learner meets the same scenario and the same criteria, including a swab deliberately hidden before the final count.
- **Comfortable and accessible.** Standing or seated, adjustable to each learner's height, subtitles for every prompt, and no camera movement that causes motion sickness.
- **Built for real training programmes.** It targets a standalone headset that needs no PC, is designed to keep working offline, and is designed to report results in the standard formats (xAPI and SCORM) that learning platforms understand.

---

## Where the project is today

**A playable prototype of the whole experience.** A learner can walk through every step from sign-in to results in a lit, furnished operating theatre with a realistic patient, scrub sink, staff and equipment. All sixteen screens of the headset interface are built, and voice prompts and sound are in place.

**Connected to its server.** Learners sign in by scanning a QR code with their phone, or with a code from their learning platform, or train as a guest. The headset scores each run itself and shows the real result. It keeps every result safe while offline and uploads it when Wi-Fi returns; from there results reach the institution's learning platform (xAPI and SCORM). The lobby shows each learner's assignments and recent attempts. The server and the reasons behind its design are described in the [backend README](../Studium%20XR%20Backend/README.md).

**It watches what the learner does.** The simulator recognises the scrub steps from hand movement, notices when a gloved hand touches something non-sterile, measures where the ports are marked and how each trocar goes in (angle, depth, force), spots an instrument drifting out of the camera's view, and will not let the patient be closed until every swab is counted. Each run is recorded and can be replayed from any angle. *Why this matters:* a score is only worth trusting if it comes from what the learner really did, and the same rules judge every learner the same way. The limits used are placeholders until a surgical educator signs them off, and they have been tested in the editor, not yet on a headset.

**Instruments behave like the real thing.** Once an instrument is through a port it pivots about that point: move your hand left and the tip swings right, exactly the reversed, levered movement that makes keyhole surgery hard to learn. Jaws close as far as you squeeze, and instruments are swapped by pulling one out and passing another in. *Why this matters:* this hand-eye skill is the main thing a beginner has to build, and it can't be learned from a simulator that lets the instrument move freely.

**The tasks are playable.** In the operating stage the learner moves six rings across a peg board, opens a tissue plane with the dissector, then clips and divides a duct, swapping instruments through the ports as they go. To finish, each port is taken out while its site is watched on the monitor, camera port last, and each site is closed with a stitch. Time, instrument path length and errors are recorded for every task. *Why this matters:* these are the building-block skills of keyhole surgery, and each rule the simulator enforces (clip before you cut, watch the port site as the port comes out) is one a trainee must make automatic before working on a patient.

**Counts and complications are real steps.** Before draping, the learner points at each instrument and swab on the back table to agree the opening count. If a trocar goes in too deep, the session branches into a bleed at the port site: draw the trocar back, press on the site and hold, then let go and watch that it stays dry. The time to control it is recorded. *Why this matters:* a count agreed at the start is the only thing the closing count can be checked against, and how a trainee responds when something goes wrong tells an educator more than a clean run does.

**A camera assistant holds the scope.** While operating, the learner asks for the view to be moved (left, right, up, down, closer, further) and the assistant moves the scope, leaving both hands free for instruments. They can also take the camera themselves. *Why this matters:* in a real operation someone else usually holds the camera, and directing them so the instruments stay in view is a skill of its own. The requests are recorded so an educator can see how the trainee managed their view.

**Guided mode coaches, Assessment mode watches.** In Guided mode a single highlight shows what to reach for next and moves on as each step is recognised, and the scrub nurse turns to the learner and says a short line when something needs a word. In Assessment mode all of that is switched off; the same rules still judge the run. *Why this matters:* learners need help while a skill is new and none when they are being tested, and both modes must measure the same thing for the score to mean anything.

**A course platform around it.** The lobby now holds a Studium XR course catalogue: 6 programs and 32 courses from theatre essentials and suturing to laparoscopy, trauma and eight surgical specialties, with readings, quizzes, VR labs, 3D equipment exploration, progress, prerequisites and certificates that anyone can verify online. About 250 NVIDIA SimReady medical models are rigged in Blender so their drawers, doors, knobs, arms, wheels and instrument jaws really move. Settings live on the palm of your hand, nothing floats in front of your face, and graphics quality adapts to the headset. Details: [Studium XR platform](Docs/StudiumXR_Platform.md).

**Coming next:**

- A web dashboard where institutions and learners follow progress. The server side of this is already built.
- Soft tissue that deforms under the instruments (needs the Obi Softbody package).
- Later: realistic tissue that tears, spoken camera commands and haptic feedback.

---

## Try it

**On a headset:** open the project in Unity 6 (6000.6.3f1) with Android support, connect a Meta Quest 3 and choose *Build And Run*.

**On a PC without a headset:** open the project in Unity and press **Play**. A built-in simulator lets you look around and use the virtual hands and controllers with the mouse and keyboard.

Setup details, controls and everything about how the project is built are in the **[Technical guide](Docs/TechnicalGuide.md)**.

---

## Credits

Created by the **Surgical Foundations team** (@DUSHIME), who designed the learning scenario, the headset interface and the production plan. Built with Unity, and with third-party 3D models listed in [Docs/ThirdPartyAssets.md](Docs/ThirdPartyAssets.md). Their sources and licences must be confirmed before any public release. Sounds, shaders and interface graphics were made for this project.

**Licence:** not yet chosen. All rights reserved by the project owners until a licence file is added.

<div align="center">

---

<sub>Surgical Foundations VR is an educational tool. It is not a medical device and is not intended for clinical use or clinical decision-making.</sub>

</div>
