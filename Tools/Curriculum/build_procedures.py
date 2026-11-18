"""
Builds the VR procedure labs: Assets/_Project/Resources/Procedures/<simulationId>.json, one per lab the course
catalogue lists (see build_catalog.py), played by Procedures/ProcedureRunner in the operating theatre.

    py -3 -I Tools/Curriculum/build_procedures.py

Each lab is a sequence of steps checked from the learner's hands: pick (take the right instrument), apply (hold it on
the right spot on the patient), operate (open/close a device's moving part), choose (a decision), talk (answer the
patient), confirm (tick off a step not simulated in detail). The script checks every equipment id against the rigged
SimReady library and every target against the anchors, and that every catalogue simulation id has a lab.

CLINICAL CONTENT STATUS: step sequences follow standard published technique (WHO checklist, BTS pleural guidance,
ATLS-style primary survey, NICE/WHO asepsis). They must be reviewed by a surgical educator before credit is given.
"""
import glob
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Resources", "Procedures")
RIGGED = os.path.join(ROOT, "Assets", "_Project", "Imports", "simready_rigged")
CATALOG = os.path.join(ROOT, "Assets", "_Project", "Resources", "Curriculum", "catalog.json")

# Targets on the patient: metres from the umbilicus (towards the head, towards the patient's right, out of the skin).
ANCHORS = {
    "umbilicus": ("umbilicus", 0.0, 0.0, 0.0, 0.05),
    "rif": ("right iliac fossa (McBurney's point)", -0.09, 0.08, -0.01, 0.06),
    "lif": ("left iliac fossa", -0.09, -0.08, -0.01, 0.06),
    "right_groin": ("right groin, above the inguinal ligament", -0.17, 0.08, -0.03, 0.06),
    "midline_upper": ("upper midline", 0.13, 0.0, 0.0, 0.06),
    "midline_lower": ("lower midline", -0.12, 0.0, -0.01, 0.06),
    "suprapubic": ("suprapubic skin crease", -0.20, 0.0, -0.03, 0.07),
    "ruq": ("right upper quadrant (hepatorenal view)", 0.17, 0.14, -0.04, 0.07),
    "luq": ("left upper quadrant (splenorenal view)", 0.17, -0.14, -0.04, 0.07),
    "pelvis_view": ("suprapubic (pelvic view)", -0.19, 0.0, -0.02, 0.07),
    "subxiphoid": ("subxiphoid (pericardial view)", 0.27, 0.0, 0.0, 0.06),
    "chest_right_anterior": ("right anterior chest, 2nd intercostal space", 0.45, 0.07, 0.02, 0.06),
    "safe_triangle": ("right safe triangle, 5th intercostal space mid-axillary line", 0.30, 0.18, -0.06, 0.06),
    "sternum": ("sternum", 0.38, 0.0, 0.02, 0.07),
    "right_antecubital": ("right antecubital fossa", 0.05, 0.26, -0.06, 0.06),
    "right_forearm": ("right forearm lesion", -0.06, 0.27, -0.05, 0.05),
    "right_ear": ("right ear", 0.68, 0.08, -0.05, 0.05),
    "nose": ("nose", 0.66, 0.0, 0.07, 0.04),
    "right_temporal": ("right temporal region", 0.72, 0.07, -0.02, 0.05),
    "right_thigh": ("right anterolateral thigh (donor site)", -0.40, 0.11, -0.03, 0.07),
    "right_leg_medial": ("medial right thigh, along the great saphenous vein", -0.42, 0.05, -0.05, 0.07),
    "right_shin": ("right shin", -0.78, 0.09, -0.04, 0.07),
    "perineum": ("urethral meatus", -0.27, 0.0, -0.08, 0.06),
    "right_hand": ("right hand", -0.12, 0.29, -0.08, 0.06),
    "pad_site": ("right thigh, patient plate site", -0.36, 0.10, -0.03, 0.08),
}

PROCEDURES = {}


def A(*names):
    return [{"name": n, "label": ANCHORS[n][0], "cranial": ANCHORS[n][1], "right": ANCHORS[n][2], "anterior": ANCHORS[n][3],
             "radius": ANCHORS[n][4]} for n in names]


def E(*specs):
    out = []
    for s in specs:
        idd, _, place = s.partition("@")
        out.append({"id": idd, "place": place or "tray", "key": idd})
    return out


def pick(title, item, hint, wrong=(), critical=False):
    return {"type": "pick", "title": title, "item": item, "hint": hint, "wrongItems": list(wrong), "critical": critical}


def apply(title, item, target, hint, hold=1.0, critical=False):
    return {"type": "apply", "title": title, "item": item, "target": target, "hint": hint, "hold": hold, "critical": critical}


def touch(title, target, hint, hold=1.0):
    return {"type": "apply", "title": title, "item": "", "target": target, "hint": hint, "hold": hold}


def operate(title, item, hint, joint="", open_=True):
    return {"type": "operate", "title": title, "item": item, "joint": joint, "open": open_, "hint": hint}


def confirm(title, hint):
    return {"type": "confirm", "title": title, "hint": hint}


def choose(title, prompt, right, *wrong, hint="", critical_wrong=()):
    """right: (text, feedback). wrong: 'text|feedback'. critical_wrong: indexes of wrong options that are unsafe."""
    ch = [{"text": right[0], "correct": True, "feedback": right[1]}]
    for i, w in enumerate(wrong):
        t, _, fb = w.partition("|")
        ch.append({"text": t, "correct": False, "feedback": fb or "Not the best choice.", "critical": i in critical_wrong})
    return {"type": "choose", "title": title, "prompt": prompt, "choices": ch, "hint": hint}


def talk(title, line, right, *wrong, hint=""):
    """right: (what the learner says, what the patient replies). wrong: 'say|feedback|reply'."""
    ch = [{"text": right[0], "correct": True, "reply": right[1], "feedback": ""}]
    for w in wrong:
        parts = w.split("|")
        ch.append({"text": parts[0], "correct": False, "feedback": parts[1] if len(parts) > 1 else "", "reply": parts[2] if len(parts) > 2 else ""})
    return {"type": "talk", "title": title, "prompt": line, "choices": ch, "hint": hint}


def proc(pid, title, summary, equipment, anchors, steps, state="anaesthetised", name="Mr Daniel Okafor", cables=()):
    for i, s in enumerate(steps):
        s["id"] = f"{pid}-s{i + 1}"
    seen, unique = set(), []
    for e in equipment:  # a kit may add a device the procedure already lists: keep the first
        if e["key"] not in seen:
            seen.add(e["key"])
            unique.append(e)
    equipment = unique
    PROCEDURES[pid] = {"id": pid, "title": title, "summary": summary, "patientState": state, "patientName": name,
                       "equipment": equipment, "anchors": A(*anchors), "cables": list(cables), "steps": steps,
                       "errorPenalty": 10, "hintPenalty": 3}


# ───────────────────────────── cables and plugs ─────────────────────────────
# Each cable: fixed end on one device, plug into a port on another device (to=) or onto the patient (anchor=).
# Heights are fractions of the device height on its learner-facing side.

def cable(key, label, frm, to=None, anchor=None, fh=0.5, fs=-0.25, th=0.5, ts=0.25, color="#2E7BD6", length=2.5):
    return {"key": key, "label": label, "from": frm, "fromHeight": fh, "fromSide": fs, "to": to or "", "toHeight": th,
            "toSide": ts, "toAnchor": anchor or "", "color": color, "length": length}


def plug(title, key, hint):
    return {"type": "plug", "title": title, "item": key, "hint": hint}


PENDANT = "pendant_medical_a01@head"   # ceiling supply pendant: mains sockets, oxygen and vacuum outlets
POWER = "#3A3D44"
GAS_O2 = "#E8E8E8"
VACUUM = "#F2C230"


def diathermy_kit():
    """Diathermy generator: mains power and the return-electrode pad on the patient."""
    eq = ["unit_electrosurgical_a01@bedside", PENDANT]
    cables = [cable("esu_power", "Diathermy mains lead", "unit_electrosurgical_a01", to="pendant_medical_a01", fh=0.3, th=0.45, ts=-0.3, color=POWER, length=4.0),
              cable("return_pad", "Return electrode (patient plate)", "unit_electrosurgical_a01", anchor="pad_site", fh=0.55, fs=0.25, color="#2E7BD6", length=3.2)]
    steps = [plug("Plug the diathermy generator into the pendant", "esu_power", "Mains lead from the generator into a socket on the ceiling pendant."),
             choose("Where does the patient plate go?", "Where should the return electrode (patient plate) be placed?",
                    ("On a well-perfused muscle mass close to the operation, such as the thigh, on dry hair-free skin",
                     "Correct: a large, well-perfused contact area keeps the current density low."),
                    "Over a bony prominence|Poor contact: a burn risk at the plate.", "Over a metal implant|Current concentrates in the metal.",
                    "Anywhere on the patient|Distance and contact both matter.", critical_wrong=(1,)),
             plug("Apply the patient plate to the thigh", "return_pad", "Full, flat contact on the thigh; check the generator's contact alarm clears.")]
    return eq, cables, steps


def suction_kit():
    eq = ["pump_suction_surgical_a01@head", PENDANT]
    cables = [cable("suction_vacuum", "Suction line to the wall vacuum", "pump_suction_surgical_a01", to="pendant_medical_a01", fh=0.75, th=0.6, ts=0.0, color=VACUUM, length=3.5),
              cable("suction_power", "Suction unit mains lead", "pump_suction_surgical_a01", to="pendant_medical_a01", fh=0.25, th=0.45, ts=0.3, color=POWER, length=3.5)]
    steps = [plug("Connect the suction to the wall vacuum", "suction_vacuum", "Canister tubing to the yellow vacuum outlet on the pendant."),
             plug("Plug in the suction unit", "suction_power", "")]
    return eq, cables, steps


def monitoring_kit():
    eq = ["monitor_patient_a01@head", PENDANT]
    cables = [cable("mon_power", "Monitor mains lead", "monitor_patient_a01", to="pendant_medical_a01", fh=0.3, th=0.45, ts=0.0, color=POWER, length=3.5),
              cable("spo2", "Pulse oximeter probe", "monitor_patient_a01", anchor="right_hand", fh=0.78, fs=-0.3, color="#4A90D9", length=3.0),
              cable("ecg", "ECG leads", "monitor_patient_a01", anchor="sternum", fh=0.78, fs=0.0, color="#D9D9D9", length=3.0),
              cable("nibp", "Blood pressure cuff", "monitor_patient_a01", anchor="right_antecubital", fh=0.78, fs=0.3, color="#7A4FD6", length=3.0)]
    steps = [plug("Plug in the patient monitor", "mon_power", ""),
             plug("Clip the oximeter probe on a finger", "spo2", "Pulse oximetry first: it is part of the WHO Sign In."),
             plug("Attach the ECG leads to the chest", "ecg", "Electrodes on the chest wall, away from the operative field."),
             plug("Put the blood pressure cuff on the arm", "nibp", "Right arm, cuff 2 cm above the elbow; not on the arm with the drip.")]
    return eq, cables, steps


def lap_tower_kit():
    eq = ["video_system_a01@head", PENDANT]
    cables = [cable("tower_power", "Tower mains lead", "video_system_a01", to="pendant_medical_a01", fh=0.2, th=0.45, ts=-0.1, color=POWER, length=4.0),
              cable("co2", "CO2 insufflation tubing", "video_system_a01", anchor="umbilicus", fh=0.45, fs=-0.3, color="#9BD3F2", length=3.5),
              cable("light", "Light cable to the laparoscope", "video_system_a01", anchor="umbilicus", fh=0.5, fs=0.0, color="#202020", length=3.5),
              cable("camera", "Camera head cable", "video_system_a01", anchor="umbilicus", fh=0.55, fs=0.3, color="#606870", length=3.5)]
    steps = [plug("Plug in the laparoscopic tower", "tower_power", ""),
             plug("Connect the light cable to the laparoscope", "light", "Light lead to the scope's light post at the camera port."),
             plug("Connect the camera head", "camera", "White balance before inserting the scope."),
             plug("Connect the CO2 tubing to the camera port", "co2", "Insufflation at 12-15 mmHg once the port is in the peritoneal cavity.")]
    return eq, cables, steps


def oxygen_kit():
    eq = [PENDANT, "cannulae_nasal_a01"]
    cables = [cable("oxygen", "Oxygen tubing", "pendant_medical_a01", anchor="nose", fh=0.55, fs=0.0, color=GAS_O2, length=3.5)]
    steps = [plug("Give oxygen: tubing from the oxygen outlet to the patient's face", "oxygen", "White oxygen outlet on the pendant; high-flow mask or nasal cannula.")]
    return eq, cables, steps


def kit(*kits):
    """Merge equipment, cables and steps from several kits (equipment de-duplicated)."""
    eq, cables, steps = [], [], []
    for k_eq, k_cables, k_steps in kits:
        for e in k_eq:
            if e not in eq:
                eq.append(e)
        cables += k_cables
        steps += k_steps
    return eq, cables, steps



# ───────────────────────────── shared building blocks ─────────────────────────────

def time_out(procedure, site_label):
    return choose("WHO Time Out", f"Before skin incision for {procedure}, the team pauses. What must be confirmed aloud?",
                  ("Patient identity, the procedure, the site and side, and antibiotics given in the last 60 minutes",
                   "Correct: the Time Out confirms who, what and where, and that prophylaxis is in."),
                  "Only that the consent form is signed|The signature alone does not prevent wrong-site surgery.",
                  "Nothing: the site was marked on the ward|Marking does not replace the Time Out.",
                  hint=f"The site is the {site_label}. All three phases of the WHO checklist are mandatory.",
                  critical_wrong=(1,))


def prep(site, label):
    return [pick("Pick up the skin preparation", "disinfectant_a01", "Alcoholic chlorhexidine, applied from the incision site outwards."),
            apply(f"Prepare the skin over the {label}", "disinfectant_a01", site,
                  "Centre outwards, wider than the incision; let it dry fully before drapes and diathermy (it is flammable).", hold=2.0)]


def local(site, label, dose_q=True):
    steps = [pick("Draw up local anaesthetic", "syringe_a02", "1 % lidocaine; check the maximum safe dose for the patient's weight first.")]
    if dose_q:
        steps.append(choose("Safe dose", "The patient weighs 70 kg. What is the maximum dose of plain 1 % lidocaine?",
                            ("21 ml (3 mg/kg = 210 mg; 1 % = 10 mg/ml)", "Correct."),
                            "49 ml|That is the dose with adrenaline (7 mg/kg).", "70 ml|That exceeds the safe dose: risk of toxicity.",
                            hint="3 mg/kg plain, 7 mg/kg with adrenaline; 1 % = 10 mg per ml.", critical_wrong=(1,)))
    steps.append(apply(f"Infiltrate the {label}", "syringe_a02", site, "Aspirate before injecting; wait for the anaesthetic to work.", hold=1.5))
    return steps


def consent_talk(proc_name, risk):
    return [
        talk("Introduce yourself and check identity", "Hello… are you the doctor doing my operation?",
             ("Hello, I'm one of the surgical team. Can you tell me your full name and date of birth?", "Daniel Okafor, fourteenth of March 1968."),
             "Yes. Let's get started straight away.|Introduce yourself and confirm identity first.|Oh… okay, I suppose.",
             hint="Name, role, then two identifiers from the patient, checked against the wristband."),
        talk("Explain and check understanding", f"What exactly are you going to do?",
             (f"I'll explain the {proc_name}, why we're doing it, the main risks such as {risk}, and the alternatives. Stop me with any questions.",
              "Thank you. That helps. What about the pain afterwards?"),
             "It's a routine thing, nothing to worry about.|Material risks and alternatives must be explained.|Right… if you say so.",
             hint="Valid consent: capacity, information (nature, purpose, material risks, alternatives), voluntary decision."),
    ]


# ───────────────────────────── 1 · Foundations ─────────────────────────────

proc("suturing-pad", "Closing a skin wound", "Close a 5 cm forearm laceration under local anaesthetic with interrupted sutures.",
     E("disinfectant_a01", "syringe_a02", "needle_surgical_a01", "instrument_forceps_a01", "instrument_scissors_a01", "instrument_artery_forceps_b01",
       "bandage_medical_a01", "box_glove_a01", "monitor_patient_a01@head"),
     ["right_forearm"],
     consent_talk("wound closure", "infection and a scar")[:1] + prep("right_forearm", "laceration") + local("right_forearm", "wound edges") + [
         choose("Choose the suture", "Which suture suits skin on the forearm?",
                ("4-0 non-absorbable monofilament on a cutting needle", "Correct: fine, low reactivity, cutting needle for dermis."),
                "1 braided absorbable on a round-bodied needle|Too thick, and round needles are for soft tissue.",
                "6-0 on a taper needle|Too fine and the wrong needle for skin."),
         pick("Load the needle", "needle_surgical_a01", "Grip the needle two thirds from the point, in the tip of the needle holder."),
         pick("Take the toothed forceps", "instrument_forceps_a01", "Toothed forceps for skin; hold them like a pen.", wrong=("instrument_artery_forceps_b01",)),
         apply("Place the first suture in the middle of the wound", "needle_surgical_a01", "right_forearm",
               "Enter at 90°, bites as far from the edge as the skin is thick, evert the edges.", hold=1.5),
         confirm("Halve the remaining gaps with interrupted sutures, about 5 mm apart", "Instrument ties, three throws, knots to one side."),
         pick("Cut the ends", "instrument_scissors_a01", "Leave 5 mm tails so they can be removed."),
         choose("Aftercare", "When should forearm sutures usually come out?", ("About 10 days", "Correct: 7–10 days on the limbs."),
                "2 days|Too early: the wound would open.", "4 weeks|Too late: suture marks and infection."),
         pick("Dress the wound", "bandage_medical_a01", "A simple dry dressing."),
     ], state="awake")

proc("minor-ops", "Excision of a skin lesion", "Elliptical excision biopsy of a forearm lesion under local anaesthetic.",
     E("tool_pen_surgical_marking_a01", "disinfectant_a01", "syringe_a02", "instrument_scalpel_handle_a01", "instrument_forceps_a01",
       "instrument_scissors_a01", "needle_surgical_a01", "medical_sample_bag_a01", "bandage_medical_a01"),
     ["right_forearm"],
     consent_talk("excision of the lump", "a scar, bleeding and the need for further surgery if the result shows it")[:2] + [
         pick("Pick up the marking pen", "tool_pen_surgical_marking_a01", "Mark the ellipse before the local anaesthetic distorts the skin."),
         apply("Mark a 3:1 ellipse along the skin tension lines", "tool_pen_surgical_marking_a01", "right_forearm", "Margins of 2 mm for a benign-looking lesion.", hold=1.5),
     ] + prep("right_forearm", "lesion") + local("right_forearm", "ellipse", dose_q=False) + [
         pick("Take the scalpel", "instrument_scalpel_handle_a01", "No. 15 blade, pen grip."),
         apply("Incise along the ellipse", "instrument_scalpel_handle_a01", "right_forearm", "Blade perpendicular to the skin, through the full dermis.", hold=2.0),
         confirm("Excise the ellipse at the level of the subcutaneous fat", "Hold the tip with toothed forceps; don't crush the specimen."),
         pick("Put the specimen in the specimen pot", "medical_sample_bag_a01", "Label it at the bedside: patient, site, date, orientation."),
         choose("Specimen labelling", "When should the specimen be labelled?",
                ("Immediately, at the bedside, with the patient's identifiers", "Correct."),
                "At the end of the list|Unlabelled specimens get mixed up.", "By the pathology lab|They cannot know whose it is.", critical_wrong=(0,)),
         confirm("Close with interrupted sutures and dress", "As in the wound closure lab."),
     ], state="awake")

_eq, _cables, _steps = kit(monitoring_kit(), diathermy_kit(), suction_kit(), lap_tower_kit())
proc("theatre-setup", "Setting up the theatre equipment",
     "Before the list starts: power, monitoring on the patient, the diathermy plate, suction to the wall vacuum and the laparoscopic tower.",
     E(*_eq), ["umbilicus", "pad_site", "right_hand", "sternum", "right_antecubital"],
     [choose("Before you plug anything in", "Which check comes first when setting up electrical equipment in theatre?",
             ("Check the equipment is serviced, undamaged and the leads intact", "Correct: damaged leads and out-of-date equipment are taken out of use."),
             "Turn everything to maximum to test it|Never: test at safe settings.", "Nothing: biomedical engineering checked it|You check before each use too.")]
     + _steps
     + [operate("Switch the suction on and check it pulls", "pump_suction_surgical_a01", "Occlude the tubing: the vacuum gauge should rise."),
        choose("Ready", "Everything is connected. What do you confirm before the patient comes in?",
               ("Alarms on, plate contact alarm clear, suction working, tower image white-balanced", "Correct."),
               "Only that the lights work|Not enough.", "Nothing: the surgeon will check|The whole team owns the set-up.")],
     cables=_cables)

# ───────────────────────────── 2 · General surgery ─────────────────────────────

_kit_open_appendicectomy = kit(monitoring_kit(), diathermy_kit(), suction_kit())
proc("open-appendicectomy", "Open appendicectomy", "A gridiron/Lanz approach to an inflamed appendix.",
     E(*_kit_open_appendicectomy[0], "disinfectant_a01", "tool_pen_surgical_marking_a01", "instrument_scalpel_handle_a01", "instrument_forceps_a01", "instrument_artery_forceps_b01",
       "instrument_retractor_a01", "instrument_scissors_a01", "instrument_suction_a01", "needle_surgical_a01", "medical_sample_bag_a01",
       "unit_electrosurgical_a01@bedside", "pump_suction_surgical_a01@head"),
     ["rif", "lif", "umbilicus", "pad_site", "right_hand", "sternum", "right_antecubital"],
     _kit_open_appendicectomy[2] + [time_out("an appendicectomy", "right iliac fossa")] + prep("rif", "right iliac fossa") + [
         pick("Take the scalpel", "instrument_scalpel_handle_a01", "No. 10 blade."),
         apply("Incise over McBurney's point", "instrument_scalpel_handle_a01", "rif",
               "One third of the way from the anterior superior iliac spine to the umbilicus.", hold=1.5, critical=True),
         choose("The muscle layers", "How are the abdominal wall muscles opened in a gridiron incision?",
                ("Split in the line of their fibres", "Correct: splitting preserves strength and nerves."),
                "Cut across with diathermy|Cutting across weakens the wall and risks hernia.", "Retracted without opening|You can't reach the peritoneum that way."),
         pick("Insert the retractor", "instrument_retractor_a01", "Langenbeck or rake retractors hold the split muscle."),
         apply("Retract the wound", "instrument_retractor_a01", "rif", "Gentle, even traction.", hold=1.5),
         choose("Find the appendix", "The caecum is delivered. How do you find the appendix base?",
                ("Follow the taeniae coli to where they converge", "Correct."), "Feel for the hardest loop|Unreliable.", "Look for the ileocaecal valve|Not a reliable landmark."),
         pick("Clamp the mesoappendix", "instrument_artery_forceps_b01", "Artery forceps on the mesoappendix, then ligate."),
         confirm("Crush, ligate and divide the appendix base", "Ligate at the base; check for bleeding."),
         pick("Send the appendix for histology", "medical_sample_bag_a01", "Label at once."),
         confirm("Close in layers and count", "The closing count must be correct before skin closure."),
     ], cables=_kit_open_appendicectomy[1])

_kit_inguinal_hernia = kit(diathermy_kit())
proc("inguinal-hernia", "Inguinal hernia repair (Lichtenstein)", "A tension-free mesh repair of a right inguinal hernia.",
     E(*_kit_inguinal_hernia[0], "disinfectant_a01", "instrument_scalpel_handle_a01", "instrument_forceps_a01", "instrument_selfretaining_retractor_a01",
       "instrument_artery_forceps_curved_b01", "instrument_scissors_a01", "needle_surgical_a01", "unit_electrosurgical_a01@bedside"),
     ["right_groin", "umbilicus", "pad_site"],
     _kit_inguinal_hernia[2] + [time_out("a right inguinal hernia repair", "right groin")] + prep("right_groin", "right groin") + [
         pick("Take the scalpel", "instrument_scalpel_handle_a01", "Incision 2 cm above and parallel to the medial two thirds of the inguinal ligament."),
         apply("Incise the right groin", "instrument_scalpel_handle_a01", "right_groin", "Wrong side is a never event: check the mark.", hold=1.5, critical=True),
         pick("Place the self-retaining retractor", "instrument_selfretaining_retractor_a01", "Opens the wound without an assistant."),
         choose("Protect the nerve", "Which nerve lies on the spermatic cord and must be protected?", ("Ilioinguinal nerve", "Correct."),
                "Femoral nerve|It is below the inguinal ligament.", "Obturator nerve|Deep in the pelvis."),
         choose("Classify the hernia", "The sac emerges lateral to the inferior epigastric vessels. It is…", ("Indirect", "Correct: through the deep ring."),
                "Direct|Direct hernias are medial to the vessels.", "Femoral|Femoral hernias are below the ligament."),
         confirm("Reduce the sac and lay the mesh on the posterior wall", "Mesh to the inguinal ligament inferiorly, conjoint tendon medially, slit round the cord."),
         confirm("Close the external oblique and skin; final count", ""),
     ], cables=_kit_inguinal_hernia[1])

_kit_laparotomy = kit(diathermy_kit(), suction_kit())
proc("laparotomy", "Midline laparotomy: opening and closing", "Open the abdomen safely and close it with a small-bites technique.",
     E(*_kit_laparotomy[0], "disinfectant_a01", "instrument_scalpel_handle_a01", "unit_electrosurgical_a01@bedside", "instrument_artery_forceps_b01", "instrument_scissors_a01",
       "instrument_selfretaining_retractor_a01", "instrument_suction_a01", "needle_surgical_a01", "pump_suction_surgical_a01@head"),
     ["midline_upper", "midline_lower", "umbilicus", "pad_site"],
     _kit_laparotomy[2] + [time_out("a laparotomy", "midline")] + prep("umbilicus", "abdomen") + [
         pick("Take the scalpel", "instrument_scalpel_handle_a01", "Skin only with the knife."),
         apply("Incise the upper midline", "instrument_scalpel_handle_a01", "midline_upper", "Skirt the umbilicus.", hold=1.5),
         apply("Continue to the lower midline", "instrument_scalpel_handle_a01", "midline_lower", "Straight line along the linea alba.", hold=1.5),
         choose("Open the peritoneum", "How is the peritoneum opened safely?",
                ("Lift it between two clips and nick it, away from the bowel", "Correct."), "Cut straight through with the knife|Risk of bowel injury.",
                "With monopolar diathermy on cut at full depth|Risk of bowel burn.", critical_wrong=(0, 1)),
         operate("Turn the suction on", "pump_suction_surgical_a01", "Suction ready before opening the abdomen."),
         pick("Place the self-retaining retractor", "instrument_selfretaining_retractor_a01", ""),
         confirm("Explore the abdomen systematically", "All four quadrants, then the pelvis, then the small bowel from the duodenojejunal flexure."),
         choose("Closing", "Which closure lowers the risk of incisional hernia?",
                ("Continuous slowly absorbable monofilament, small bites 5 mm by 5 mm, suture:wound length at least 4:1", "Correct."),
                "Interrupted large bites 2 cm apart|Higher hernia rate.", "Rapidly absorbable braided suture|Loses strength too early."),
         confirm("Final count and skin closure", ""),
     ], cables=_kit_laparotomy[1])

proc("bowel-anastomosis", "Bowel anastomosis", "Join two ends of small bowel with a single-layer hand-sewn anastomosis.",
     E("instrument_clamp_a01", "instrument_forceps_a01", "instrument_artery_forceps_b01", "instrument_scissors_a01", "needle_surgical_a01", "stapler_surgical_a01",
       "instrument_suction_a01"),
     ["umbilicus", "midline_lower"],
     [
         choose("Requirements", "Which is NOT a requirement for a healthy anastomosis?", ("Tension across the join to keep it straight", "Correct: tension causes leaks."),
                "A good blood supply|This is essential.", "Healthy bowel ends|This is essential."),
         pick("Apply the soft bowel clamps", "instrument_clamp_a01", "Non-crushing clamps, away from the cut ends."),
         pick("Hold the bowel", "instrument_forceps_a01", "Non-toothed forceps for bowel.", wrong=("instrument_artery_forceps_b01",)),
         apply("Place the mesenteric corner stitch", "needle_surgical_a01", "umbilicus", "Seromuscular, extramucosal, round-bodied needle.", hold=1.5),
         confirm("Run the posterior and anterior walls (single-layer extramucosal)", "Bites about 5 mm, 5 mm apart."),
         operate("Fire the stapler (alternative technique)", "stapler_surgical_a01", "Squeeze the handle fully to fire.", joint="hand_01"),
         choose("Check the join", "After a circular stapled anastomosis you must check…", ("That both tissue doughnuts are complete", "Correct."),
                "That the stapler is empty|Irrelevant to the anastomosis.", "That the bowel is distended|Not a check."),
         choose("Leak", "Day 4: tachycardia, fever and abdominal pain. What do you suspect first?", ("Anastomotic leak", "Correct: act on it early."),
                "Normal recovery|Never assume.", "Wound infection only|Rule out a leak first.", critical_wrong=(0,)),
     ])

_kit_lap_appendicectomy = kit(diathermy_kit(), lap_tower_kit())
proc("lap-appendicectomy", "Laparoscopic appendicectomy", "Port placement, base control and safe extraction.",
     E(*_kit_lap_appendicectomy[0], "disinfectant_a01", "tool_pen_surgical_marking_a01", "instrument_scalpel_handle_a01", "video_system_a01@head", "unit_electrosurgical_a01@bedside",
       "medical_sample_bag_a01", "needle_surgical_a01"),
     ["umbilicus", "lif", "suprapubic", "rif", "pad_site"],
     _kit_lap_appendicectomy[2] + [time_out("a laparoscopic appendicectomy", "abdomen, right iliac fossa")] + prep("umbilicus", "abdomen") + [
         pick("Mark the port sites", "tool_pen_surgical_marking_a01", ""),
         apply("Camera port: umbilicus (open Hasson entry)", "tool_pen_surgical_marking_a01", "umbilicus", "Open entry under vision.", hold=1.0),
         apply("Working port: left iliac fossa", "tool_pen_surgical_marking_a01", "lif", "Lateral to the rectus, away from the epigastric vessels.", hold=1.0),
         apply("Working port: suprapubic", "tool_pen_surgical_marking_a01", "suprapubic", "Bladder emptied first.", hold=1.0),
         choose("Pressure", "Insufflation pressure for an adult?", ("12–15 mmHg", "Correct."), "30 mmHg|Impairs venous return.", "3 mmHg|No working space.", critical_wrong=(0,)),
         choose("The base", "How is the appendix base secured?", ("Two endoloops or a stapler at the base", "Correct."),
                "Diathermy only|Risk of stump leak.", "Leave it open|Faecal fistula.", critical_wrong=(1,)),
         pick("Bag the appendix", "medical_sample_bag_a01", "Extract in a bag to avoid port-site infection."),
         confirm("Remove the ports under vision and close the umbilical fascia", "Camera port last; check each site for bleeding."),
     ], cables=_kit_lap_appendicectomy[1])

# Follows the Children's Hospital Colorado film used in MIS-201 (general anaesthesia, umbilical camera port, CO2
# pneumoperitoneum, systematic pelvic survey, recognising subtle lesions, biopsy and excision or ablation, ports out
# under vision, day-case discharge), with the decisions a trainee makes at each stage.
_kit_diag_lap = kit(diathermy_kit(), lap_tower_kit())
proc("diagnostic-laparoscopy", "Diagnostic laparoscopy for endometriosis",
     "Look, recognise and treat: a 19-year-old with cyclical pelvic pain unresponsive to medical treatment.",
     E(*_kit_diag_lap[0], "disinfectant_a01", "tool_pen_surgical_marking_a01", "instrument_scalpel_handle_a01", "device_endoscope_a01",
       "medical_sample_bag_a01", "needle_surgical_a01"),
     ["umbilicus", "lif", "rif", "suprapubic", "pad_site"],
     _kit_diag_lap[2] + [
         choose("Why operate?", "Ultrasound of her pelvis was normal. Why is a diagnostic laparoscopy still indicated?",
                ("Imaging often misses superficial peritoneal endometriosis; only seeing and biopsying lesions confirms it",
                 "Correct: a normal scan does not exclude endometriosis, especially the subtle lesions seen in adolescents."),
                "A normal scan rules it out, so this is unnecessary|Superficial disease is invisible on ultrasound.",
                "To remove the uterus|Fertility-sparing: the aim is to diagnose and treat lesions."),
         time_out("a diagnostic laparoscopy", "abdomen and pelvis"),
         choose("Positioning", "The patient is anaesthetised. How is she positioned for a pelvic laparoscopy?",
                ("Lithotomy with the legs supported, arms tucked, ready for head-down (Trendelenburg) tilt once the ports are in",
                 "Correct: head-down tilt lets the bowel fall out of the pelvis so it can be seen."),
                "Prone|The pelvis must face the camera.", "Steep head-up tilt|The bowel would fall into the pelvis and hide it.",
                hint="Padding at the knees and calves; check the common peroneal nerve is not compressed.", critical_wrong=()),
         confirm("Empty the bladder (in-out catheter) before the suprapubic port", "A full bladder sits in the path of the lower ports."),
     ] + prep("umbilicus", "abdomen") + [
         pick("Mark the port sites", "tool_pen_surgical_marking_a01", "Three ports of about 5 mm."),
         apply("Camera port: umbilicus", "tool_pen_surgical_marking_a01", "umbilicus", "Hidden in the navel: the best cosmetic result.", hold=1.0),
         apply("Working port: left iliac fossa", "tool_pen_surgical_marking_a01", "lif", "Lateral to the rectus sheath, away from the inferior epigastric vessels.", hold=1.0),
         apply("Working port: right iliac fossa", "tool_pen_surgical_marking_a01", "rif", "Mirror image of the left; inserted under vision.", hold=1.0),
         choose("Entry", "How do you enter the abdomen for the camera port in this slim young patient?",
                ("Open (Hasson) entry at the umbilicus, or Veress at Palmer's point with careful checks",
                 "Correct: in a slim patient the aorta is only a few centimetres below the umbilicus."),
                "Blind trocar straight down with force|The great vessels lie close beneath: a catastrophic injury risk.",
                "Through the suprapubic site first|The camera port goes in first, then the others under vision.", critical_wrong=(0,)),
         choose("Pneumoperitoneum", "To what pressure do you insufflate CO2?",
                ("12–15 mmHg (lower if tolerated)", "Correct: enough room to see, without impairing venous return."),
                "30 mmHg|Impairs venous return and ventilation.", "3 mmHg|No working space.", critical_wrong=(0,)),
         pick("Insert the laparoscope", "device_endoscope_a01", "Warm the lens against fogging; white balance done; look around the entry site first for injury."),
         choose("Survey order", "The scope is in. What do you do first?",
                ("Inspect the whole abdomen (liver, diaphragm, appendix, bowel), then tilt head-down and survey the pelvis systematically",
                 "Correct: a systematic survey, so nothing is missed and entry injuries are found."),
                "Go straight to the ovaries|A quick look misses disease and entry injuries.",
                "Start treating the first lesion seen|Map all the disease before treating any of it."),
         choose("The pelvic survey", "Which pelvic sites must be inspected?",
                ("Uterus, both ovaries and tubes, ovarian fossae, uterosacral ligaments, pouch of Douglas, bladder peritoneum, pelvic side walls, sigmoid and appendix",
                 "Correct: the most common sites are the ovaries, uterosacral ligaments and pouch of Douglas."),
                "Only the uterus|Most lesions are on the peritoneum and ovaries.", "Only the ovaries|Peritoneal lesions would be missed."),
         choose("Recognising lesions", "In adolescents, endometriosis often looks like…",
                ("Subtle clear or red vesicles and flame-like lesions, rather than the classic black 'powder-burn' spots",
                 "Correct: early lesions are subtle; look closely, with the scope near the peritoneum."),
                "Only large black cysts|Those are typical of older, advanced disease.", "Nothing visible: it cannot be seen|It can: it must be looked for carefully."),
         pick("Biopsy a lesion and send it for histology", "medical_sample_bag_a01", "Histology confirms the diagnosis; label the specimen with its site."),
         choose("Treatment", "A lesion lies on the left pelvic side wall, close to where the ureter runs. How do you treat it?",
                ("Identify the ureter first, then excise the lesion (lift the peritoneum away) or ablate it with care",
                 "Correct: know where the ureter is before any energy is used on the side wall."),
                "Deep diathermy directly over it|Thermal spread can injure the ureter.",
                "Leave all disease and refer|Superficial disease is treated at the same operation where safe.", critical_wrong=(0,)),
         choose("Document", "What do you record before finishing?",
                ("The site, size and appearance of every lesion and what was done, with photographs, and a stage if used",
                 "Correct: the findings guide follow-up and hormonal treatment."),
                "Only 'endometriosis found'|Too little to guide care.", "Nothing until histology returns|Record findings now."),
         confirm("Release the CO2, remove the ports under vision and close the umbilical fascia",
                 "Check each port site for bleeding; let all the gas out to reduce shoulder-tip pain."),
         apply("Close the skin with absorbable subcuticular sutures", "needle_surgical_a01", "umbilicus", "They dissolve and need no removal.", hold=1.5),
         choose("Going home", "In recovery she asks why her shoulder hurts. What do you tell her?",
                ("Leftover gas irritates the diaphragm; it settles in 2–3 days and she can go home today",
                 "Correct: referred pain via the phrenic nerve (C3–5); day-case discharge is usual."),
                "It means something went wrong|It is expected after laparoscopy.", "She dislocated it on the table|No."),
     ], name="Ms Amara Nwosu", cables=_kit_diag_lap[1])

# ───────────────────────────── 3 · Trauma & emergency ─────────────────────────────

_kit_trauma_bay = kit(monitoring_kit(), oxygen_kit())
proc("trauma-bay", "Trauma primary survey", "A motorcyclist with a bleeding thigh wound and falling blood pressure.",
     E(*_kit_trauma_bay[0], "bandage_medical_a03", "monitor_patient_a01@head", "oximeter_pulse_a01", "blood_bag_a01", "warmer_infusion_a01@bedside", "IV_pole_a01@head",
       "syringe_a03", "trolley_emergency_a01@foot", "ultrasound_b01@bedside"),
     ["right_thigh", "ruq", "right_antecubital", "nose", "sternum", "right_hand"],
     _kit_trauma_bay[2] + [
         choose("First priority", "The patient is bleeding heavily from the right thigh. What comes first?",
                ("Control the catastrophic haemorrhage", "Correct: <C>ABCDE."), "Airway assessment|Catastrophic bleeding comes first.",
                "Full secondary survey|Not now.", critical_wrong=(1,)),
         pick("Take a pressure dressing", "bandage_medical_a03", "Direct pressure, then packing; a tourniquet if that fails."),
         apply("Apply firm pressure to the wound", "bandage_medical_a03", "right_thigh", "Hold firm pressure.", hold=3.0),
         talk("Airway: is he talking?", "Uh… my leg… it hurts…", ("Sir, you're in hospital. What's your name? Can you tell me where it hurts?", "Daniel… my leg, and my chest hurts when I breathe."),
              "Don't talk, just lie still.|Speech tells you the airway is patent and the brain perfused.|…"),
         pick("Put on the pulse oximeter", "oximeter_pulse_a01", "Monitoring: SpO2, ECG, blood pressure."),
         touch("Assess breathing: listen to the chest", "ruq", "Look, feel, listen; percuss both sides.", hold=1.5),
         choose("Circulation", "HR 128, BP 88/50, cool peripheries. Which fluid?",
                ("Blood products early, warmed, with tranexamic acid within 3 hours", "Correct: haemostatic resuscitation."),
                "Several litres of crystalloid|Dilutes clotting factors and worsens bleeding.", "No fluids|He is shocked and bleeding.", critical_wrong=(0,)),
         operate("Open the blood warmer", "warmer_infusion_a01", "Warm all blood products.", ),
         touch("Two large-bore cannulas: right antecubital fossa", "right_antecubital", "14–16 G, take bloods and a crossmatch.", hold=1.5),
         pick("Bedside ultrasound (eFAST)", "ultrasound_b01", "Look for free fluid and pneumothorax."),
         confirm("Disability and Exposure: GCS, pupils, glucose, full exposure, keep warm", ""),
     ], state="awake", cables=_kit_trauma_bay[1])

proc("chest-drain", "Chest drain insertion", "Open (blunt dissection) insertion of a chest drain for a traumatic pneumothorax.",
     E("disinfectant_a01", "syringe_a02", "instrument_scalpel_handle_a01", "instrument_artery_forceps_curved_b01", "drainage_a01", "drainage_pumpSystem_a01@bedside",
       "needle_surgical_a01", "bandage_medical_a01"),
     ["safe_triangle", "chest_right_anterior", "umbilicus"],
     consent_talk("chest drain", "bleeding, infection and organ injury")[:1] + [
         choose("Where?", "Where do you insert the drain?",
                ("The safe triangle: 5th intercostal space, between latissimus dorsi and pectoralis major", "Correct."),
                "2nd intercostal space, mid-clavicular line|That is for needle decompression.", "Below the nipple, anterior chest|Risk of liver and spleen injury.",
                critical_wrong=(1,)),
     ] + prep("safe_triangle", "safe triangle") + local("safe_triangle", "safe triangle down to the pleura", dose_q=False) + [
         pick("Take the scalpel", "instrument_scalpel_handle_a01", ""),
         apply("Incise over the upper border of the rib", "instrument_scalpel_handle_a01", "safe_triangle",
               "The neurovascular bundle runs under each rib.", hold=1.5, critical=True),
         pick("Blunt dissection with curved forceps", "instrument_artery_forceps_curved_b01", "Spread, don't stab; then a finger sweep."),
         apply("Open the pleura with the forceps", "instrument_artery_forceps_curved_b01", "safe_triangle", "Controlled, over the rib.", hold=1.5),
         pick("Insert the drain", "drainage_a01", "No trocar: guide the tube with forceps."),
         operate("Connect the underwater seal", "drainage_pumpSystem_a01", "Check the water level swings with breathing."),
         confirm("Secure the drain and get a chest X-ray", "Suture, dressing; X-ray to check the position."),
     ], state="awake")

proc("efast", "eFAST scan in trauma", "Look for free fluid and pneumothorax with bedside ultrasound.",
     E("ultrasound_b01@bedside", "ultrasound_a01"),
     ["ruq", "luq", "pelvis_view", "subxiphoid", "chest_right_anterior"],
     [
         pick("Take the ultrasound probe", "ultrasound_a01", "Curvilinear probe for the abdomen."),
         apply("Right upper quadrant: hepatorenal view", "ultrasound_a01", "ruq", "Morison's pouch is where free fluid collects first.", hold=2.0),
         apply("Left upper quadrant: splenorenal view", "ultrasound_a01", "luq", "Aim more posterior and higher than on the right.", hold=2.0),
         apply("Pelvic view", "ultrasound_a01", "pelvis_view", "Transverse and longitudinal behind the bladder.", hold=2.0),
         apply("Subxiphoid pericardial view", "ultrasound_a01", "subxiphoid", "Point towards the left shoulder.", hold=2.0),
         apply("Anterior chest: lung sliding", "ultrasound_a01", "chest_right_anterior", "Absent sliding suggests pneumothorax.", hold=2.0),
         choose("Interpret", "There is a black stripe in Morison's pouch. This is…", ("Free fluid, likely blood", "Correct: anechoic fluid."),
                "Normal kidney fat|Fat is bright.", "Air|Air scatters the beam and looks bright."),
         choose("Act", "The patient is unstable with a positive FAST. Next?", ("Call for surgery / damage control laparotomy", "Correct."),
                "CT scan first|An unstable patient should not go to CT.", "Repeat FAST in an hour|Too late.", critical_wrong=(0, 1)),
     ], state="awake")

proc("vascular-access", "Peripheral and central venous access", "Cannulate a vein using a vein finder, and plan a Seldinger central line.",
     E("device_vein_finder_a01", "disinfectant_a01", "syringe_a03", "catheter_system_a01", "IV_pole_a01@head", "pump_infusion_a01@head", "bandage_medical_a01",
       "ultrasound_b01@bedside"),
     ["right_antecubital", "right_hand"],
     [
         talk("Explain the cannula", "Do I really need another needle?",
              ("I need a drip to give you fluids and medicines. It's a sharp scratch, and I'll use this light to find a good vein first.", "Okay, go ahead."),
              "Yes. Hold still.|Explain why and what to expect.|Fine…"),
         pick("Find a vein with the vein finder", "device_vein_finder_a01", "Near-infrared light shows the veins under the skin."),
         apply("Scan the antecubital fossa", "device_vein_finder_a01", "right_antecubital", "Choose a straight, bouncy vein.", hold=2.0),
     ] + prep("right_antecubital", "chosen vein") + [
         pick("Take the cannula", "catheter_system_a01", "Bevel up, 15–30°; watch for flashback, then advance the cannula off the needle."),
         apply("Cannulate the vein", "catheter_system_a01", "right_antecubital", "Flashback, lower, advance a few mm, slide the cannula in.", hold=1.5),
         pick("Flush the cannula", "syringe_a03", "Saline flush: no swelling, no pain."),
         operate("Open the infusion pump door and load the line", "pump_infusion_a01", "Check drug, dose, rate and line against the prescription."),
         choose("Central line", "During a Seldinger central line insertion you must…", ("Never let go of the guidewire", "Correct: a lost wire can embolise."),
                "Remove the wire before dilating|The dilator goes over the wire.", "Insert without ultrasound|Ultrasound guidance is the standard.", critical_wrong=(0,)),
     ], state="awake")

# ───────────────────────────── 4 · Specialties ─────────────────────────────

proc("catheterisation", "Male urethral catheterisation", "Aseptic catheterisation for acute urinary retention.",
     E("box_glove_a01", "disinfectant_b01", "syringe_a04", "catheter_system_a01", "drainage_a01", "bandage_medical_a01"),
     ["perineum", "suprapubic"],
     [
         talk("Consent", "It hurts down there. I haven't passed water since last night.",
              ("I'm sorry, that's painful. A tube into the bladder will relieve it straight away. It's uncomfortable but quick. Is that alright?",
               "Yes, please, anything to stop this."),
              "We'll just get on with it.|Explain and ask for consent.|Uh… what are you doing?"),
         touch("Feel for the enlarged bladder", "suprapubic", "A tender, dull suprapubic mass.", hold=1.5),
         pick("Put on sterile gloves", "box_glove_a01", "Aseptic non-touch technique."),
         pick("Clean the meatus", "disinfectant_b01", ""),
         apply("Clean the urethral meatus", "disinfectant_b01", "perineum", "Retract the foreskin; clean away from the meatus.", hold=1.5),
         pick("Instil anaesthetic gel", "syringe_a04", "Lidocaine gel; wait a few minutes."),
         pick("Take the catheter", "catheter_system_a01", "Hold the penis upright to straighten the urethra."),
         apply("Advance the catheter", "catheter_system_a01", "perineum", "Advance to the hilt.", hold=2.0),
         choose("Balloon", "When do you inflate the balloon?", ("Only once urine drains, and the catheter is fully inserted", "Correct."),
                "As soon as you feel resistance|Inflating in the urethra causes serious injury.", "Before inserting|Impossible and dangerous.",
                critical_wrong=(0,)),
         pick("Connect the drainage bag", "drainage_a01", "Record the residual volume; replace the foreskin."),
     ], state="awake")

proc("ent-exam", "ENT examination and epistaxis", "Examine the ear, then manage an anterior nosebleed.",
     E("device_otoscope_a01", "headlight_surgical_a01", "instrument_nasal_forceps_b01", "bandage_medical_a01", "instrument_mirror_a01"),
     ["right_ear", "nose"],
     [
         talk("History", "My ear has been aching for three days, and now my nose keeps bleeding.",
              ("Let's look at your ear first, then I'll sort out the nosebleed. Sit forward and pinch the soft part of your nose for me.", "Like this? Okay."),
              "Tilt your head back.|Tilting back makes the patient swallow blood.|*coughs*"),
         pick("Pick up the otoscope", "device_otoscope_a01", "Largest speculum that fits comfortably."),
         apply("Examine the right ear", "device_otoscope_a01", "right_ear", "Adults: pull the pinna up and back to straighten the canal.", hold=2.0),
         choose("Findings", "The drum is red and bulging. This is most likely…", ("Acute otitis media", "Correct."), "Wax|Wax would hide the drum.", "Normal|A normal drum is pearly grey."),
         pick("Put on the headlight", "headlight_surgical_a01", "Both hands free."),
         choose("Epistaxis", "Where does most epistaxis come from?", ("Little's area on the anterior septum", "Correct."), "The posterior nasal cavity|Less common, more serious.",
                "The sinuses|No."),
         touch("Firm pressure on the soft nose for 15 minutes", "nose", "Pinch the soft part, sitting forward.", hold=3.0),
         pick("Nasal forceps for packing if pressure fails", "instrument_nasal_forceps_b01", "Cautery or packing next."),
         apply("Insert the nasal pack", "instrument_nasal_forceps_b01", "nose", "Along the floor of the nose, not upwards.", hold=1.5),
     ], state="awake")

proc("ortho-fixation", "Fracture fixation basics", "Reduce and fix a tibial shaft fracture: instruments and principles.",
     E("disinfectant_a01", "instrument_bone_clamp_b01", "instrument_bone_forceps_b01", "instrument_hand_drill_b01", "tool_power_surgical_b01", "instrument_bone_mallet_b01",
       "instrument_bone_rongeurs_b01", "instrument_caliper_a01", "instrument_raspatory_a01", "Xray_machine_buckyStand_a01@foot"),
     ["right_shin"],
     [time_out("fixation of the right tibia", "right leg")] + prep("right_shin", "right shin") + [
         choose("Describe", "How should a fracture be described?", ("Bone, location, pattern, displacement, and the state of the skin", "Correct."),
                "Just 'broken leg'|Not useful for planning.", "By the mechanism only|Mechanism helps but doesn't describe the fracture."),
         pick("Strip the periosteum minimally with the raspatory", "instrument_raspatory_a01", "Preserve the blood supply."),
         pick("Reduce with the bone clamp", "instrument_bone_clamp_b01", "Clamp across the fracture to hold the reduction."),
         apply("Hold the reduction", "instrument_bone_clamp_b01", "right_shin", "Restore length, alignment and rotation.", hold=2.0),
         pick("Drill the screw hole", "tool_power_surgical_b01", "Drill with a guide; measure with the depth gauge."),
         apply("Drill across the fracture", "tool_power_surgical_b01", "right_shin", "Perpendicular to the fracture for a lag screw.", hold=2.0),
         choose("Stability", "A lag screw and plate give…", ("Absolute stability with primary bone healing", "Correct."), "Relative stability|That is a nail or cast.",
                "No stability|They do stabilise."),
         operate("Check the X-ray", "Xray_machine_buckyStand_a01", "Two views at right angles."),
     ])

proc("vascular-anastomosis", "Vascular anastomosis", "Control a vessel and perform an end-to-side anastomosis.",
     E("instrument_clamp_a01", "instrument_forceps_a01", "instrument_dilator_a01", "instrument_scalpel_handle_a01", "needle_surgical_a01", "syringe_a05"),
     ["right_groin", "right_leg_medial"],
     [time_out("a right femoral exposure", "right groin")] + [
         choose("Control", "Before opening the artery you must have…", ("Proximal and distal control", "Correct."), "Only proximal control|Back-bleeding will flood the field.",
                "No clamps, to avoid injury|Uncontrolled bleeding.", critical_wrong=(1,)),
         pick("Give heparin before clamping", "syringe_a05", "Systemic heparin, then wait before clamping."),
         pick("Apply the vascular clamps", "instrument_clamp_a01", ""),
         apply("Clamp above and below", "instrument_clamp_a01", "right_groin", "Atraumatic vascular clamps.", hold=1.5),
         pick("Arteriotomy", "instrument_scalpel_handle_a01", "No. 11 blade, then Potts scissors."),
         choose("Suture direction", "On the arterial side, the needle passes…", ("Inside to outside", "Correct: tacks down the intima."),
                "Outside to inside|Can lift an intimal flap.", "Either way|The direction matters."),
         pick("Run the anastomosis", "needle_surgical_a01", "Fine polypropylene, heel and toe first."),
         confirm("Flush, release the clamps in order and check for bleeding and a distal pulse", ""),
     ])

proc("skin-graft", "Split-thickness skin graft", "Harvest a split-skin graft from the thigh and apply it to a clean wound.",
     E("disinfectant_a01", "instrument_skingrafting_knife_a01", "instrument_spatula_a01", "instrument_forceps_a01", "stapler_surgical_a01", "bandage_medical_a04"),
     ["right_thigh", "right_shin"],
     [time_out("a split skin graft to the right shin", "right thigh donor, right shin recipient")] + prep("right_thigh", "donor thigh") + [
         choose("Which graft?", "Large clean wound on the shin. Which graft?", ("Split-thickness, meshed if needed", "Correct: covers large areas, takes reliably."),
                "Full-thickness|Limited donor sites; used for face and hands.", "No graft, primary closure|Too big to close."),
         pick("Lubricate the donor site and set the knife depth", "instrument_skingrafting_knife_a01", "Thin graft about 0.3 mm."),
         apply("Harvest from the thigh", "instrument_skingrafting_knife_a01", "right_thigh", "Even, back-and-forth strokes with counter-traction.", hold=3.0),
         pick("Spread the graft", "instrument_spatula_a01", "Dermis down, onto the recipient bed."),
         apply("Lay the graft on the shin wound", "instrument_spatula_a01", "right_shin", "No folds; dermal side down.", hold=2.0),
         choose("Graft take", "What makes a graft fail?", ("A collection (blood or seroma) under it, shear, or infection", "Correct."),
                "Bright light|No.", "Applying it dermis down|That is correct technique."),
         pick("Dress with a tie-over or compression dressing", "bandage_medical_a04", "Immobilise for 5 days."),
     ])

proc("burr-hole", "Emergency burr hole", "Recognise an extradural haematoma and plan an emergency decompression.",
     E("instrument_hand_drill_b01", "instrument_scalpel_handle_a01", "instrument_raspatory_a01", "instrument_punche_a01", "instrument_suction_a01", "monitor_patient_a01@head"),
     ["right_temporal"],
     [
         choose("Diagnosis", "Lucid interval after a blow to the temple; CT shows a lens-shaped collection. This is…", ("Extradural haematoma", "Correct."),
                "Subdural haematoma|Subdurals are crescent-shaped.", "Subarachnoid haemorrhage|Blood in the cisterns, not a lens."),
         choose("Who operates?", "What is the definitive treatment?", ("Craniotomy by a neurosurgeon; an emergency burr hole only if transfer is impossible", "Correct."),
                "Burr hole by anyone, always|Last resort only.", "Observe|An expanding extradural is fatal."),
         touch("Locate the site: above the zygoma, in front of the ear, on the side of the clot", "right_temporal", "Check the side on the scan.", hold=1.5),
         pick("Incise the scalp", "instrument_scalpel_handle_a01", ""),
         pick("Elevate the periosteum", "instrument_raspatory_a01", ""),
         pick("Make the burr hole with the hand drill", "instrument_hand_drill_b01", "Perforator, then burr; perpendicular to the skull."),
         apply("Drill over the clot", "instrument_hand_drill_b01", "right_temporal", "Stop when the inner table gives.", hold=3.0, critical=True),
         pick("Evacuate with suction", "instrument_suction_a01", ""),
     ])

_kit_caesarean = kit(diathermy_kit(), suction_kit())
proc("caesarean", "Caesarean section", "Steps, team and safety in a category 2 caesarean section.",
     E(*_kit_caesarean[0], "disinfectant_a01", "instrument_scalpel_handle_a01", "instrument_retractor_a01", "instrument_scissors_a01", "pump_suction_surgical_a01@head",
       "warmer_infusion_a01@bedside", "monitor_patient_a01@head", "syringe_a01"),
     ["suprapubic", "umbilicus", "pad_site"],
     _kit_caesarean[2] + [time_out("a caesarean section", "lower abdomen")] + prep("suprapubic", "lower abdomen") + [
         pick("Take the scalpel", "instrument_scalpel_handle_a01", "Pfannenstiel incision, two fingers above the symphysis."),
         apply("Transverse suprapubic incision", "instrument_scalpel_handle_a01", "suprapubic", "Curved, in the skin crease.", hold=1.5),
         pick("Bladder retractor", "instrument_retractor_a01", "Reflect and protect the bladder."),
         choose("Uterine incision", "The uterus is opened…", ("Transversely in the lower segment", "Correct: less bleeding, stronger scar."),
                "Vertically through the fundus|Classical incision: high rupture risk.", "Anywhere convenient|No."),
         operate("Suction ready for liquor", "pump_suction_surgical_a01", ""),
         confirm("Deliver the baby and the placenta", "Note the time; hand the baby to the midwife/neonatologist."),
         pick("Give the uterotonic", "syringe_a01", "Oxytocin as per protocol."),
         choose("Postpartum haemorrhage", "Heavy bleeding after delivery. First actions?", ("Uterine massage, uterotonics, tranexamic acid, call for help", "Correct."),
                "Wait and see|PPH kills quickly.", "Close quickly and leave|Bleeding continues."),
     ], cables=_kit_caesarean[1])

_kit_cardiac = kit(monitoring_kit())
proc("cardiac", "After cardiac surgery: tamponade", "Recognise and manage tamponade on the cardiac ICU.",
     E(*_kit_cardiac[0], "monitor_patient_a01@head", "lung_ventilation_artificial_a01@head", "drainage_pumpSystem_a01@bedside", "defibrillator_a01@foot", "pump_infusion_a01@head",
       "ultrasound_b01@bedside", "ultrasound_a01"),
     ["sternum", "subxiphoid", "right_hand", "right_antecubital"],
     _kit_cardiac[2] + [
         choose("Recognise", "6 h after CABG: BP falling, CVP rising, drains suddenly stop. Suspect…", ("Cardiac tamponade", "Correct."),
                "Hypovolaemia|CVP would fall.", "Normal recovery|No."),
         operate("Check the chest drains", "drainage_pumpSystem_a01", "Blocked drains can cause tamponade."),
         pick("Echo to confirm", "ultrasound_a01", ""),
         apply("Subxiphoid view", "ultrasound_a01", "subxiphoid", "Pericardial collection, right-sided collapse.", hold=2.0),
         choose("Management", "Tamponade with haemodynamic compromise after cardiac surgery needs…", ("Urgent re-sternotomy", "Correct."),
                "More fluids and observation only|Temporises at best.", "Discharge from ICU|No.", critical_wrong=(1,)),
         operate("Open the defibrillator lid, pads ready", "defibrillator_a01", "In case of arrest: follow the cardiac surgery arrest protocol (CALS)."),
         touch("Sternum: the re-opening site", "sternum", "Re-sternotomy within 5 minutes of arrest after cardiac surgery.", hold=1.5),
     ], cables=_kit_cardiac[1])

# ───────────────────────────── write + validate ─────────────────────────────

def main():
    rigged = {os.path.splitext(os.path.basename(p))[0] for p in glob.glob(os.path.join(RIGGED, "*", "*.glb"))}
    problems = []
    for pid, p in PROCEDURES.items():
        keys = {e["key"] for e in p["equipment"]}
        anchors = {a["name"] for a in p["anchors"]}
        for e in p["equipment"]:
            if rigged and e["id"] not in rigged:
                problems.append(f"{pid}: equipment {e['id']} is not rigged")
        cables = {c["key"]: c for c in p.get("cables", [])}
        for c in cables.values():
            if c["from"] not in keys:
                problems.append(f"{pid}: cable {c['key']} starts at {c['from']}, not in equipment")
            if c["to"] and c["to"] not in keys:
                problems.append(f"{pid}: cable {c['key']} plugs into {c['to']}, not in equipment")
            if c["toAnchor"] and c["toAnchor"] not in anchors:
                problems.append(f"{pid}: cable {c['key']} goes onto {c['toAnchor']}, not in anchors")
            if not c["to"] and not c["toAnchor"]:
                problems.append(f"{pid}: cable {c['key']} has nowhere to plug in")
        for s in p["steps"]:
            if s["type"] == "plug" and s["item"] not in cables:
                problems.append(f"{pid}/{s['title']}: cable {s['item']} not in cables")
            if s["type"] in ("pick", "operate") and s["item"] not in keys:
                problems.append(f"{pid}/{s['title']}: item {s['item']} not in equipment")
            if s["type"] == "apply":
                if s["target"] not in anchors:
                    problems.append(f"{pid}/{s['title']}: target {s['target']} not in anchors")
                if s["item"] and s["item"] not in keys:
                    problems.append(f"{pid}/{s['title']}: item {s['item']} not in equipment")
            if s["type"] in ("choose", "talk") and sum(c["correct"] for c in s["choices"]) != 1:
                problems.append(f"{pid}/{s['title']}: needs exactly one correct choice")
            for w in s.get("wrongItems", []):
                if w not in keys:
                    problems.append(f"{pid}/{s['title']}: wrong item {w} not in equipment")
    wanted = set()
    if os.path.exists(CATALOG):
        cat = json.load(open(CATALOG, encoding="utf-8"))
        builtin = {"lap-foundations", "lap-prep", "lap-access", "lap-operate", "lap-close", "skills-lab"}
        wanted = {l["simulationId"] for c in cat["courses"] for m in c["modules"] for l in m["lessons"]
                  if l.get("simulationId") and l["simulationId"] not in builtin}
        for w in sorted(wanted - set(PROCEDURES)):
            problems.append(f"catalogue lists simulation '{w}' but no procedure lab exists")
    if problems:
        raise SystemExit("Problems:\n  " + "\n  ".join(problems))
    os.makedirs(OUT, exist_ok=True)
    for pid, p in PROCEDURES.items():
        with open(os.path.join(OUT, pid + ".json"), "w", encoding="utf-8") as f:
            json.dump(p, f, indent=1, ensure_ascii=False)
    steps = sum(len(p["steps"]) for p in PROCEDURES.values())
    plugs = sum(1 for p in PROCEDURES.values() for st in p["steps"] if st["type"] == "plug")
    print(f"{len(PROCEDURES)} procedure labs, {steps} steps ({plugs} plug-in steps) -> {os.path.normpath(OUT)}")
    print("covers every catalogue simulation:", sorted(wanted) == sorted(set(PROCEDURES) & wanted))


if __name__ == "__main__":
    main()
