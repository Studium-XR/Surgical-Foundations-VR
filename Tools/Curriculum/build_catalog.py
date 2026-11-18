"""
Builds the Studium XR course catalogue (Assets/_Project/Resources/Curriculum/catalog.json) from the compact course
definitions below. Run after editing:

    py -3 -I Tools/Curriculum/build_catalog.py

Structure: programs (specialisation tracks) -> courses -> modules -> lessons. Every course gets the same shape a MOOC
learner expects: readings, knowledge checks with feedback, hands-on VR labs or 3D equipment exploration, and a final
assessment that counts towards the certificate.

CLINICAL CONTENT STATUS: drafted from standard published guidance (WHO Surgical Safety Checklist, WHO/NICE SSI
guidance, FLS/FES/FUSE programme outlines, ACS/APDS/ASE and ISCP curricula). It must be reviewed and signed off by a
qualified surgical educator before learners use it for credit, exactly like the scoring thresholds.
"""
import json
import os
import re

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "_Project", "Resources", "Curriculum", "catalog.json")
# The API embeds the same file (GET /catalog), so the headset and the server never disagree about a course.
API_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "Studium-XR-Backend", "src", "SurgicalFoundations.Api", "Catalog", "catalog.json")
VERSION = 3  # 3: five questions per quiz, diagnostic laparoscopy module in MIS-201


def slug(s):
    return re.sub(r"[^a-z0-9]+", "-", s.lower()).strip("-")


def q(prompt, right, *wrong, why=""):
    """One multiple-choice question: the right answer, then the distractors (each 'text|feedback' or just text)."""
    opts = [{"text": right, "correct": True, "feedback": why or "Correct."}]
    for w in wrong:
        text, _, fb = w.partition("|")
        opts.append({"text": text, "correct": False, "feedback": fb or "Not quite: " + (why or "review the reading.")})
    return {"prompt": prompt, "options": opts}


# Lesson builders. Each returns a dict; ids are filled in when the course is assembled.
def read(title, body, minutes=8):
    return {"title": title, "kind": "Reading", "minutes": minutes, "summary": body.split(". ")[0] + ".", "content": body}


def quiz(title, *questions, minutes=6, pass_score=70):
    return {"title": title, "kind": "Quiz", "minutes": minutes, "summary": f"{len(questions)} questions with feedback.",
            "questions": list(questions), "passScore": pass_score}


def lab(title, sim, summary, minutes=15, pass_score=70):
    return {"title": title, "kind": "VrLab", "minutes": minutes, "summary": summary, "simulationId": sim, "passScore": pass_score}


def assess(title, sim, summary, minutes=20, pass_score=75):
    return {"title": title, "kind": "VrAssessment", "minutes": minutes, "summary": summary, "simulationId": sim, "passScore": pass_score}


def explore(title, equipment, summary, minutes=10):
    return {"title": title, "kind": "Explore", "minutes": minutes, "summary": summary, "equipment": equipment}


def module(title, summary, *lessons):
    return {"title": title, "summary": summary, "lessons": list(lessons)}


COURSES = []


def course(code, title, subtitle, specialty, level, hours, cme, outcomes, skills, frameworks, modules, prereq=(), description=""):
    COURSES.append({
        "id": slug(code), "code": code, "title": title, "subtitle": subtitle, "specialty": specialty, "level": level,
        "description": description or subtitle, "outcomes": outcomes, "prerequisites": [slug(p) for p in prereq],
        "skills": skills, "frameworks": frameworks, "hours": hours, "cmeCredits": cme, "modules": modules,
    })


# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
# 1 · SURGICAL FOUNDATIONS
# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

course("SF-101", "Operating Theatre Essentials", "Find your way round theatre, work safely in the team and keep the field sterile.",
       "Surgical Foundations", "Foundation", 6, 6,
       ["Describe the layout of an operating theatre and the role of each team member",
        "Run the three phases of the WHO Surgical Safety Checklist",
        "Perform a surgical hand scrub, closed gowning and gloving",
        "Set up and maintain a sterile field and recognise when it is breached",
        "Agree an opening count and complete a closing count"],
       ["Surgical scrub", "Closed gloving", "Sterile field", "Counts", "WHO checklist"],
       ["ACS/APDS/ASE Resident Prep", "ISCP Core Surgical Training", "WHO Surgical Safety Checklist"],
       [module("The theatre and the team", "Who does what, where, and why it matters to the patient.",
               read("Theatre layout and zones",
                    "An operating suite is divided into unrestricted, semi-restricted and restricted zones. Theatre attire is worn in the semi-restricted zone; "
                    "masks are added in the restricted zone, where the sterile field is open. Air flows from the cleanest area (over the table) outwards, "
                    "so doors stay closed and traffic is kept to a minimum. Learn where the scrub sink, anaesthetic room, sterile store and dirty utility sit "
                    "in your own hospital: on day one you should be able to reach each without crossing the sterile field."),
               read("Roles in the theatre team",
                    "The operating surgeon and assistant are scrubbed; the scrub practitioner owns the instruments, the sterile field and the counts; the "
                    "circulating practitioner is unscrubbed and fetches, opens and records; the anaesthetist and anaesthetic practitioner own the airway, "
                    "drugs and monitoring. Anyone, including a student, may and must speak up about a sterility breach or a safety concern."),
               explore("Meet the theatre equipment",
                       ["table_surgical_a01", "light_surgical_ceiling_arm_a01", "anesthesia_machine_a01", "monitor_patient_a01", "video_system_a01",
                        "trolley_instrument_b01", "pendant_medical_a01", "pump_suction_surgical_a01", "unit_electrosurgical_a01"],
                       "Walk round the core equipment of a theatre; open, raise and move each working part."),
               lab("Set up the theatre equipment", "theatre-setup",
                   "Plug in the generator, monitor, suction and tower; put the monitoring and the diathermy plate on the patient."),
               quiz("Check: theatre and team",
                    q("Who is primarily responsible for the instrument and swab count?", "The scrub practitioner, with the circulator",
                      "The anaesthetist", "The operating surgeon alone|The surgeon confirms, but the count is run by the scrub and circulating practitioners.",
                      why="The scrub and circulating practitioners count aloud together and record it; the surgeon is told and confirms."),
                    q("A student notices a gloved assistant touch the unsterile light handle. What should they do?", "Say so immediately",
                      "Say nothing; students should not interrupt|Everyone in theatre has a duty to speak up about a breach.",
                      "Mention it at the end of the case|By then the contamination may have reached the wound.",
                      why="Any breach is called out at once so the glove can be changed before it touches the field."))),
        module("Safety checklist", "Sign in, time out, sign out.",
               read("The WHO Surgical Safety Checklist",
                    "Sign In happens before induction: identity, site, procedure and consent are confirmed, the site is marked, the anaesthesia machine and "
                    "pulse oximeter are checked, and allergies, airway risk and expected blood loss are reviewed. Time Out happens before skin incision: the "
                    "whole team introduces itself, confirms patient, site and procedure, antibiotic prophylaxis within the last 60 minutes, anticipated "
                    "critical events and imaging. Sign Out happens before the patient leaves: the procedure recorded, counts correct, specimens labelled, "
                    "equipment problems and recovery concerns handed over.", 10),
               quiz("Check: the checklist",
                    q("When is the Time Out performed?", "Immediately before skin incision", "Before induction of anaesthesia|That is the Sign In.",
                      "Before the patient leaves theatre|That is the Sign Out.", why="Time Out is the last pause before the knife touches skin."),
                    q("Antibiotic prophylaxis for most clean-contaminated cases is ideally given…", "Within 60 minutes before incision",
                      "After closure|Too late to be in the tissues when contamination happens.", "The evening before surgery",
                      why="Tissue levels must be adequate at the moment of incision; most agents are given within 60 minutes before it."))),
        module("Scrubbing in", "Hand antisepsis, gown and gloves.",
               read("Surgical hand antisepsis",
                    "Remove jewellery, keep nails short and unvarnished. A first scrub of the day starts with a wash to remove debris, cleaning under the "
                    "nails. With an antiseptic scrub (chlorhexidine or povidone-iodine) scrub hands then forearms to 2 cm above the elbow for the time in "
                    "your local policy (typically 2–5 minutes), keeping hands above elbows so water runs off the elbows. Alcohol-based surgical hand rubs "
                    "are an accepted alternative on clean, dry hands. Dry with a sterile towel from fingertips to elbow, one side of the towel per arm."),
               lab("Scrub at the sink", "lap-prep", "Scrub with the demonstration hands; each step is recognised from your hand movement."),
               read("Closed gowning and gloving",
                    "Pick up the gown by the inside of the neckband, let it unfold away from you, slide your arms in without letting your hands leave the "
                    "cuffs. Glove through the gown sleeve (closed technique), then tie the gown with a sterile turn. Your sterile area is the front of the "
                    "gown from chest to table level and the sleeves from 5 cm above the elbow to the cuff: hands stay in it, clasped in front."),
               quiz("Check: scrubbing",
                    q("Which part of a sterile gown is considered sterile?", "Front from chest to table level, and the sleeves",
                      "The whole gown including the back|The back cannot be watched and is not sterile.", "Only the cuffs",
                      why="Front chest-to-table and sleeves to just above the elbow are the sterile zone."),
                    q("During the scrub, hands are held…", "Above the elbows", "Below the elbows|Water would run from the unscrubbed arm onto the hands.",
                      "At any height as long as the brush is used", why="Water must drain from the clean hands towards the elbows."))),
        module("Sterile field and counts", "Back table, drapes and counts.",
               read("Maintaining the sterile field",
                    "Only sterile items touch sterile items. Sterile tables are sterile only at table height; anything that drops below the waist or the "
                    "table edge is contaminated. Never turn your back on the field, never reach across an unsterile area, and keep two sterile people "
                    "front-to-front or back-to-back when passing. If in doubt, consider it contaminated and replace it."),
               read("Counts",
                    "Swabs, sharps and instruments are counted aloud by two people before the procedure (the opening count), before closing a cavity, "
                    "before skin closure and at the end. Swabs are counted in their packs of five, separated, with the radio-opaque marker visible. A "
                    "discrepancy stops closure until the item is found or an X-ray is taken."),
               lab("Back table check and opening count", "lap-prep", "Check the sterile back table, count instruments and swabs, then drape the patient."),
               assess("Assessment: prepare for surgery", "lap-prep", "Scrub, gown, glove, count and drape with no guidance.")),
        ])

course("SF-102", "Surgical Instruments and Handling", "Know every instrument on the tray, hold it properly and pass it safely.",
       "Surgical Foundations", "Foundation", 5, 5,
       ["Name the instrument families and give examples of each",
        "Hold a scalpel, forceps, scissors and needle holder correctly",
        "Pass and receive instruments safely, including sharps",
        "Choose an instrument for a task and explain why"],
       ["Instrument recognition", "Instrument grips", "Sharps safety"],
       ["ACS/APDS/ASE Resident Prep", "ISCP Core Surgical Training"],
       [module("Instrument families", "Cutting, grasping, clamping, retracting and suction.",
               read("The five families",
                    "Cutting: scalpels, scissors (Mayo for tougher tissue and sutures, Metzenbaum for delicate dissection), bone saws and rongeurs. "
                    "Grasping: toothed forceps for skin and fascia, non-toothed (atraumatic) forceps for bowel and vessels. Clamping: artery forceps "
                    "(haemostats) with a ratchet, Kocher clamps with teeth, bowel clamps. Retracting: hand-held (Langenbeck, Deaver, rake) and "
                    "self-retaining (Weitlaner, Balfour). Suction: Yankauer for open fields, Poole for the abdomen, fine suction for small spaces."),
               explore("The instrument tray",
                       ["instrument_scalpel_handle_a01", "instrument_scissors_a01", "instrument_forceps_a01", "instrument_artery_forceps_b01",
                        "instrument_artery_forceps_curved_b01", "instrument_clamp_a01", "instrument_retractor_a01", "instrument_selfretaining_retractor_a01",
                        "instrument_suction_a01", "instrument_bipolar_dissecting_scissors_a01", "needle_surgical_a01", "tray_sterilization_instrument_c02"],
                       "Pick up each instrument, open its jaws and lock the ratchets; its name and use appear as you hold it."),
               quiz("Check: name the family",
                    q("Which forceps should be used to hold bowel wall?", "Non-toothed (atraumatic) forceps", "Toothed forceps|Teeth puncture bowel.",
                      "A Kocher clamp|Kochers have teeth and crush tissue.", why="Bowel and vessels are handled with atraumatic instruments."),
                    q("A self-retaining retractor is chosen when…", "The assistant's hands are needed for other tasks",
                      "The wound is being closed", "Suction is required", why="Self-retaining retractors hold the wound open without an assistant."))),
        module("Handling", "Grips, passing and sharps.",
               read("Grips",
                    "Scalpel: the 'dinner knife' grip for long skin incisions with a No. 10 blade; the pen grip for fine work with a No. 15. Forceps: "
                    "held like a pen, never in the palm. Scissors and needle holders: tripod grip with thumb and ring finger in the rings and the index "
                    "finger on the shaft for stability; cut with the tips. Never use the fingers to pick up needles."),
               read("Passing instruments and sharps safety",
                    "Instruments are passed handle first into the surgeon's palm, curved instruments with the curve matching the surgeon's hand. Sharps "
                    "are passed in a neutral zone (a kidney dish) rather than hand to hand, scalpels are loaded and unloaded with a needle holder, and "
                    "every needle is accounted for at the end of the count."),
               lab("Instrument handling at the skills station", "skills-lab", "Pick, grip and pass the open-surgery instruments at the skills-lab station."),
               quiz("Check: handling",
                    q("Scalpel blades should be loaded…", "With a needle holder, never with fingers", "With the fingers, carefully",
                      "By the circulating practitioner", why="Using a needle holder keeps fingers away from the edge."))),
        ], prereq=["SF-101"])

course("SF-103", "Wound Closure and Suturing", "Choose the right suture and needle, tie secure knots and close skin neatly.",
       "Surgical Foundations", "Foundation", 8, 8,
       ["Choose suture material and needle for a given tissue",
        "Tie a two-handed reef knot and an instrument tie",
        "Place simple interrupted, mattress and subcuticular sutures",
        "Describe the alternatives: staples, adhesive strips and tissue glue"],
       ["Knot tying", "Interrupted sutures", "Mattress sutures", "Subcuticular closure"],
       ["ACS/APDS/ASE Resident Prep", "ISCP Core Surgical Training"],
       [module("Sutures and needles", "Materials and their uses.",
               read("Choosing a suture",
                    "Absorbable sutures (e.g. polyglactin, poliglecaprone, PDS) lose strength over weeks and suit deep layers and subcuticular skin. "
                    "Non-absorbable sutures (nylon, polypropylene) keep strength and suit skin sutures that will be removed and vascular anastomoses. "
                    "Monofilaments slide and resist infection but need more throws; braided sutures handle well but can harbour bacteria. Sizes run from "
                    "0 (thick) to 10-0 (finest): 3-0 or 4-0 for most skin, 5-0/6-0 on the face. Cutting needles go through skin; round-bodied needles "
                    "through bowel and soft tissue."),
               explore("Needles, holders and closure devices",
                       ["needle_surgical_a01", "needle_48mm_a01", "needle_68mm_a01", "instrument_ligature_needle_a01", "instrument_forceps_a01",
                        "instrument_scissors_a01", "stapler_surgical_a01", "bandage_medical_a01"],
                       "Look at needle shapes and sizes, a ligature needle and a skin stapler."),
               quiz("Check: materials",
                    q("Which needle is used for skin?", "Cutting (triangular) needle", "Round-bodied needle|Round-bodied needles are for soft tissue and bowel.",
                      "Blunt needle", why="A cutting edge passes through tough dermis."),
                    q("A monofilament suture needs…", "More throws to be secure", "Fewer throws than braided", "No knot",
                      why="Monofilaments are slippery, so knots need extra throws."))),
        module("Knots and stitches", "From the reef knot to subcuticular closure.",
               read("Knot tying",
                    "A square (reef) knot is two throws in opposite directions laid flat; a surgeon's knot adds a double first throw so the first tie holds "
                    "under tension. Instrument ties save suture and are quick for skin. Tension each throw by moving the hands in the plane of the knot and "
                    "beyond it; never saw the suture or lift the tissue."),
               read("Stitches",
                    "Simple interrupted: enter at 90°, equal bites either side (about as far from the edge as the skin is thick), evert the edges. "
                    "Vertical mattress: far-far then near-near for eversion in thin or lax skin. Horizontal mattress: spreads tension. Subcuticular: "
                    "continuous in the dermis for the best cosmetic result. Approximate, don't strangulate: tissue swells after closure."),
               lab("Suturing practice pad", "suturing-pad", "Place interrupted and mattress sutures on a skin pad and tie instrument knots."),
               assess("Assessment: close the wound", "suturing-pad", "Close a 5 cm wound with interrupted sutures, scored on spacing, eversion and knot security.")),
        ], prereq=["SF-102"])

course("SF-104", "Asepsis and Infection Prevention", "Prevent surgical site infection from the ward to the wound.",
       "Surgical Foundations", "Foundation", 3, 3,
       ["Explain how surgical site infection develops and is classified",
        "Apply the SSI prevention bundle: skin prep, hair removal, normothermia, glucose, antibiotics",
        "Decontaminate and sterilise instruments and explain the sterile-processing cycle"],
       ["Skin preparation", "Draping", "Sterile processing"],
       ["WHO Global Guidelines for SSI Prevention", "NICE NG125"],
       [module("Surgical site infection", "Classification and the prevention bundle.",
               read("Wound classes and SSI",
                    "Wounds are classed clean, clean-contaminated, contaminated or dirty, and the risk of infection rises with each. SSIs are superficial, "
                    "deep or organ/space and are counted up to 30 days after surgery (90 with an implant). The prevention bundle: no routine hair removal "
                    "(clippers, not razors, if needed), alcohol-based chlorhexidine skin preparation allowed to dry, prophylactic antibiotics within 60 "
                    "minutes before incision, normothermia, glucose control and minimal theatre traffic."),
               read("Skin preparation and draping",
                    "Prepare from the incision site outwards in widening areas, never back towards the centre, covering enough skin for drains and an "
                    "extended incision. Alcohol-based preparations are flammable: let them dry fully before diathermy and drapes. Drapes are placed from "
                    "the incision outwards and never moved towards it once placed."),
               explore("Sterile processing",
                       ["container_system_sterilising_a02", "container_system_sterilising_e01", "tray_sterilization_instrument_c03", "disinfectant_a01",
                        "disinfectant_d01", "sink_surgical_a01", "cleaning_soapDispenser_a01"],
                       "Open sterilising containers and trays and see how a tray is assembled."),
               quiz("Check: SSI",
                    q("If hair must be removed, it should be removed with…", "Clippers, close to the time of surgery", "A razor the night before|Razors cause micro-abrasions that raise infection risk.",
                      "Depilatory cream in theatre", why="Clipping avoids skin trauma; shaving increases SSI."),
                    q("Why must alcoholic skin prep dry before diathermy?", "It is flammable", "It inactivates diathermy", "Wet skin cannot be draped",
                      why="Pooled alcohol can ignite and cause a burn."))),
        ], prereq=["SF-101"])

course("SF-105", "Perioperative Patient Safety", "Consent, marking, VTE, anticoagulation, fasting and handover.",
       "Surgical Foundations", "Foundation", 4, 4,
       ["Take valid informed consent and know when you may not",
        "Prescribe VTE prophylaxis and manage anticoagulants around surgery",
        "Apply fasting guidance and hand over safely using SBAR"],
       ["Consent", "VTE prophylaxis", "SBAR handover"],
       ["ISCP Core Surgical Training", "GMC Decision making and consent"],
       [module("Before the operation", "Consent and preparation.",
               read("Informed consent",
                    "Consent is valid when the patient has capacity, is informed (the nature of the procedure, its purpose, material risks, alternatives "
                    "including doing nothing) and decides voluntarily. The person taking consent must be able to perform the procedure or be trained to "
                    "consent for it. Material risks are those a reasonable person in the patient's position would want to know, or this patient would."),
               read("VTE, anticoagulants and fasting",
                    "Assess every patient for venous thromboembolism and bleeding risk; offer mechanical and/or pharmacological prophylaxis by risk. "
                    "Plan anticoagulant and antiplatelet interruption with the prescriber, weighing thrombotic against bleeding risk. Typical elective "
                    "fasting: food until 6 hours, clear fluids until 2 hours before anaesthesia (check your local policy)."),
               quiz("Check: consent and safety",
                    q("Which of these is required for valid consent?", "Capacity, information and voluntariness", "A signature only|The signature is evidence, not consent itself.",
                      "Agreement of the next of kin|Adults with capacity decide for themselves.", why="Consent is a process: capacity, information, voluntary choice."),
                    q("SBAR stands for…", "Situation, Background, Assessment, Recommendation", "Safety, Bleeding, Airway, Response", "Sign, Brief, Act, Review",
                      why="SBAR structures a handover or a call for help."))),
        ], prereq=["SF-101"])

course("SF-106", "Energy Devices in Surgery", "Use monopolar, bipolar and advanced energy safely.",
       "Surgical Foundations", "Intermediate", 3, 3,
       ["Explain how monopolar and bipolar diathermy work",
        "Prevent burns: return electrode, alcohol prep, insulation failure and capacitive coupling",
        "Choose cut, coagulation or advanced bipolar/ultrasonic energy for a task"],
       ["Diathermy", "Energy safety"], ["SAGES FUSE"],
       [module("Electrosurgery", "Principles and safety.",
               read("Monopolar and bipolar",
                    "In monopolar diathermy current flows from the active electrode through the patient to a return pad; in bipolar it flows only between "
                    "the two tips. Cut uses a continuous low-voltage waveform that vaporises cells; coagulation uses an interrupted high-voltage waveform. "
                    "Burns occur at a poorly applied return pad, through pooled alcohol, by direct coupling to another instrument, through insulation "
                    "failure or by capacitive coupling in laparoscopy. Bipolar is preferred near nerves, in fingers and penis, and around pacemakers."),
               explore("The electrosurgical unit", ["unit_electrosurgical_a01", "instrument_bipolar_dissecting_scissors_a01", "pump_suction_surgical_a01"],
                       "Explore the generator, its leads and a bipolar instrument."),
               quiz("Check: energy",
                    q("Which current path does bipolar diathermy use?", "Only between the two tips of the instrument",
                      "Through the patient to a return pad|That is monopolar.", "Through the operating table", why="Bipolar does not need a return electrode."),
                    q("A laparoscopic burn away from the field can be caused by…", "Capacitive coupling or insulation failure",
                      "Using too low a power", "Bipolar forceps", why="Stray current can leak through damaged insulation or by capacitance."))),
        ], prereq=["SF-101"])

course("SF-107", "Anaesthesia and Monitoring for Surgeons", "What the anaesthetist is doing, and what the numbers on the monitor mean.",
       "Surgical Foundations", "Foundation", 4, 4,
       ["Read a patient monitor: ECG, SpO2, blood pressure, capnography",
        "Calculate safe local anaesthetic doses and recognise toxicity",
        "Recognise the deteriorating patient in theatre"],
       ["Monitoring", "Local anaesthesia", "ASA grading"], ["ISCP Core Surgical Training"],
       [module("The anaesthetic side of the screen", "Machines, monitors and drugs.",
               read("Monitoring",
                    "Minimum monitoring during anaesthesia: ECG, pulse oximetry, non-invasive blood pressure, capnography and anaesthetic gas analysis, "
                    "with temperature for longer cases. A falling end-tidal CO2 can mean a disconnection, a pulmonary embolism or low cardiac output; a "
                    "rising heart rate with falling blood pressure in surgery is bleeding until proven otherwise."),
               read("Local anaesthetic doses",
                    "Commonly quoted maximum doses: lidocaine 3 mg/kg plain and 7 mg/kg with adrenaline; bupivacaine 2 mg/kg. A 1 % solution contains "
                    "10 mg/ml. Toxicity starts with perioral tingling, tinnitus and agitation, then seizures and cardiovascular collapse; treat with "
                    "airway support and intravenous lipid emulsion. Always check your local formulary."),
               explore("The anaesthetic workstation",
                       ["anesthesia_machine_a01", "lung_ventilation_artificial_a01", "monitor_patient_a01", "pump_infusion_a01", "pump_system_infusion_a01",
                        "oximeter_pulse_a01", "anesthesia_mask_a01", "defibrillator_a01", "device_blood_pressure_monitor_a01"],
                       "Explore the anaesthetic machine, ventilator, infusion pumps and monitoring."),
               quiz("Check: monitoring and drugs",
                    q("What is the maximum dose of plain lidocaine for a 70 kg adult (3 mg/kg)?", "210 mg (21 ml of 1 %)", "490 mg|That is the dose with adrenaline.",
                      "70 mg", why="3 mg/kg × 70 kg = 210 mg; 1 % = 10 mg/ml, so 21 ml."),
                    q("Sudden loss of the capnography trace most commonly means…", "Disconnection or airway problem", "The patient is waking up",
                      "A normal finding during laparoscopy", why="Always check the circuit and the airway first."))),
        ])

course("SF-108", "Human Factors and Non-Technical Skills", "Situational awareness, decision making, communication and leadership in theatre.",
       "Surgical Foundations", "Foundation", 3, 3,
       ["Describe the NOTSS categories and give an example of each",
        "Use closed-loop communication and speak up with graded assertiveness",
        "Run a structured debrief after a critical event"],
       ["NOTSS", "Communication", "Crisis resource management"], ["NOTSS (RCSEd)"],
       [module("Non-technical skills", "Why good surgeons are good team members.",
               read("NOTSS",
                    "The Non-Technical Skills for Surgeons framework has four categories: situation awareness (gather, understand, anticipate), "
                    "decision making (consider options, select and communicate, review), communication and teamwork (exchange information, establish "
                    "shared understanding, coordinate), and leadership (set standards, support others, cope with pressure)."),
               read("Speaking up",
                    "Use closed-loop communication: the instruction, the read-back, the confirmation. When concerned, escalate with graded assertiveness, "
                    "for example the PACE steps: Probe, Alert, Challenge, Emergency. A stop word agreed at the briefing lets anyone halt the procedure."),
               quiz("Check: NOTSS",
                    q("Closed-loop communication means…", "The receiver repeats the instruction and the sender confirms it", "Only the team leader speaks",
                      "Instructions are written down", why="Read-back catches errors before they are acted on."))),
        ])

# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
# 2 · MINIMALLY INVASIVE SURGERY
# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

course("MIS-201", "Laparoscopic Fundamentals", "Equipment, pneumoperitoneum, safe entry and port placement.",
       "Minimally Invasive Surgery", "Intermediate", 6, 6,
       ["Set up a laparoscopic tower and troubleshoot the image",
        "Explain the physiology of pneumoperitoneum",
        "Choose and perform a safe entry technique and place ports by triangulation",
        "Recognise and manage a port-site bleed"],
       ["Laparoscopic entry", "Port placement", "Camera navigation"], ["SAGES FLS", "ISCP Core Surgical Training"],
       [module("The tower", "Light, camera, insufflation.",
               read("The laparoscopic stack",
                    "The stack holds the camera control unit, light source, insufflator, recorder and monitor. A poor image is usually a dirty or fogged "
                    "lens, white balance not done, a loose light cable or blood in the field. The insufflator delivers CO2, typically to 12–15 mmHg in "
                    "adults; higher pressure reduces venous return and splints the diaphragm."),
               explore("Laparoscopic equipment", ["video_system_a01", "monitor_surgical_a01", "device_endoscopy_system_b01", "device_endoscope_a01", "unit_electrosurgical_a01"],
                       "Explore the video system, monitor and endoscopy stack."),
               lab("Connect the tower", "theatre-setup", "Light cable, camera head, CO2 tubing and power, then check the image."),
               quiz("Check: the tower",
                    q("A typical adult intra-abdominal pressure for laparoscopy is…", "12–15 mmHg", "30–40 mmHg|Dangerous: it would impair venous return.",
                      "2–4 mmHg", why="12–15 mmHg balances working space against cardiorespiratory effects."))),
        module("Access", "Entry and ports.",
               read("Entry and port placement",
                    "Open (Hasson) entry at the umbilicus is preferred by many guidelines; Veress needle and optical entry are alternatives with their own "
                    "precautions. Working ports are placed under vision, away from the epigastric vessels, triangulated so the instruments meet the "
                    "target at about 60° and the camera sits between them. Insert trocars perpendicular to the skin and then angle towards the target, "
                    "with controlled force."),
               lab("Place the ports", "lap-access", "Mark the port sites and insert the trocars; angle, depth and force are measured."),
               assess("Assessment: safe access", "lap-access", "Mark and place all ports with no guidance; a deep trocar triggers a port-site bleed to manage.")),
        module("Diagnostic laparoscopy", "Looking inside to diagnose and treat in one operation: endometriosis as the example.",
               {"title": "Video: diagnostic laparoscopy for endometriosis", "kind": "Video", "minutes": 6,
                "summary": "A 3-minute patient-facing film from Children's Hospital Colorado, with notes.",
                "content":
                    "Watch on a phone or computer: youtube.com/watch?v=KJfQnlGgBgA (Children's Hospital Colorado, 3 min).\n\n"
                    "Why it is done: endometriosis is endometrial-type tissue growing outside the uterus, where it bleeds and inflames with each "
                    "cycle and causes pelvic pain. Imaging (pelvic ultrasound or MRI) is used first to rule out other causes, but only looking "
                    "inside confirms the diagnosis, so diagnostic laparoscopy is the reference test. It is usually a day case.\n\n"
                    "The operation: under general anaesthesia the surgeon makes two or three incisions of about 5 mm. One takes the camera with "
                    "its light cable; the others take working instruments. The abdomen is insufflated with CO2 to lift the wall and give room "
                    "and a clear view. The pelvis is inspected systematically (uterus, ovaries, pouch of Douglas, uterosacral ligaments, "
                    "bladder peritoneum, bowel). Lesions found can be treated in the same operation: excised or ablated with laser or "
                    "electrosurgery. The ports come out under vision and the small wounds are closed with absorbable subcuticular sutures that "
                    "need no removal. It takes about 1–2 hours depending on the disease found.\n\n"
                    "Afterwards: home the same day once recovered. Expect nausea from the anaesthetic, mild port-site pain, and shoulder-tip "
                    "pain for 2–3 days from residual CO2 irritating the diaphragm. No shower for 24 hours, no bath or swimming for a week, most "
                    "activities within a week. Findings are discussed at follow-up, often with hormonal treatment to limit recurrence.\n\n"
                    "Link to this course: the same tower set-up, CO2 pneumoperitoneum and safe port placement you practise in the labs."},
               lab("Procedure lab: diagnostic laparoscopy", "diagnostic-laparoscopy",
                   "Set up, enter safely, survey the pelvis, recognise subtle endometriosis, biopsy and treat it, and close.", minutes=20),
               quiz("Check: diagnostic laparoscopy")),
        ], prereq=["SF-101", "SF-102"])

course("MIS-202", "Laparoscopic Manual Skills", "Peg transfer, precision cutting, ligating loop and knot tying.",
       "Minimally Invasive Surgery", "Intermediate", 8, 8,
       ["Transfer objects between hands through ports with the fulcrum effect",
        "Dissect and clip structures under the scope",
        "Direct a camera assistant to keep instruments in view"],
       ["Peg transfer", "Fulcrum effect", "Clip and divide"], ["SAGES FLS manual skills"],
       [module("Hand–eye skills", "The fulcrum effect and depth perception on a flat screen.",
               read("The fulcrum effect",
                    "A laparoscopic instrument pivots at the abdominal wall: move the hand left and the tip goes right, move it up and the tip goes down. "
                    "The ratio of inside to outside length scales tip movement. Depth comes from cues on a 2D screen: shadows, size and instrument "
                    "contact. Deliberate practice of standard tasks (peg transfer, precision cutting, ligating loop, intra- and extracorporeal knots) "
                    "builds these skills measurably."),
               lab("Peg transfer, dissection and clipping", "lap-operate", "Move six rings, open a tissue plane, then clip and divide a duct."),
               assess("Assessment: laparoscopic tasks", "lap-operate", "Complete the tasks unaided; time, path length and errors are scored.")),
        ], prereq=["MIS-201"])

course("MIS-203", "Laparoscopic Cholecystectomy: Full Procedure", "From the scrub sink to closing, the complete operation.",
       "Minimally Invasive Surgery", "Advanced", 10, 10,
       ["Achieve the critical view of safety before clipping",
        "Clip and divide the cystic duct and artery safely",
        "Remove ports under vision and complete the closing count"],
       ["Critical view of safety", "Clip and divide", "Port closure"], ["SAGES Safe Cholecystectomy", "ISCP General Surgery"],
       [module("Safe cholecystectomy", "The critical view of safety.",
               read("The critical view of safety",
                    "Before any structure is clipped: the hepatocystic triangle is cleared of fat and fibrous tissue, the lower third of the gallbladder "
                    "is separated from the liver bed, and exactly two structures are seen entering the gallbladder. If it cannot be achieved, stop and "
                    "consider a bail-out: subtotal cholecystectomy, cholangiography or calling for help."),
               quiz("Check: CVS",
                    q("How many structures should be seen entering the gallbladder for the critical view of safety?", "Two", "Three", "One",
                      why="Cystic duct and cystic artery: two structures only."),
                    q("If the critical view cannot be achieved you should…", "Consider a bail-out strategy or call for help",
                      "Clip the most likely duct", "Convert to open immediately in every case|Conversion is one option, not the only one.",
                      why="Never clip without the CVS; bail-out options exist."))),
        module("Close", "Ports out and counts."),
        module("The whole operation", "Prep, access, operate and close in one session.",
               lab("Closing: count and remove the ports", "lap-close", "Count every swab and instrument and remove the ports under vision, camera last."),
               assess("Assessment: full procedure", "lap-foundations", "The whole scenario in Assessment mode, scored per stage.", minutes=25)),
        ], prereq=["MIS-202", "SF-103"])

course("MIS-204", "Laparoscopic Appendicectomy", "Diagnose, operate and manage the complicated appendix.",
       "Minimally Invasive Surgery", "Advanced", 6, 6,
       ["Assess suspected appendicitis using clinical scores and imaging",
        "Describe port positions and steps of laparoscopic appendicectomy",
        "Manage the base of a gangrenous appendix and an abscess"],
       ["Diagnostic laparoscopy", "Appendix base ligation"], ["ISCP General Surgery"],
       [module("Appendicitis", "From diagnosis to operation.",
               read("Diagnosis",
                    "Classic migration of pain to the right iliac fossa, anorexia, fever and localised peritonism; scores such as Alvarado or AIR stratify "
                    "risk. Ultrasound is first line in children and pregnancy; CT in adults when the diagnosis is unclear."),
               read("Operation",
                    "Umbilical camera port, left iliac fossa and suprapubic working ports. Divide the mesoappendix, secure the base with endoloops or a "
                    "stapler, remove the appendix in a bag. Wash out pus locally, avoid spreading it. Send the specimen for histology."),
               lab("Laparoscopic skills refresher", "lap-operate", "Warm up on the laparoscopic tasks before the procedure lab."),
               {"title": "Procedure lab: laparoscopic appendicectomy", "kind": "VrLab", "minutes": 20, "simulationId": "lap-appendicectomy",
                "summary": "The full procedure in the virtual theatre.", "passScore": 70}),
        ], prereq=["MIS-202"])

course("MIS-205", "Robotic Surgery Fundamentals", "Docking, instrument control and safety in robotic-assisted surgery.",
       "Minimally Invasive Surgery", "Advanced", 4, 4,
       ["Describe the components of a robotic system and the team roles",
        "Explain docking, port placement and emergency undocking",
        "Recognise the loss of haptic feedback and compensate visually"],
       ["Robotic docking", "Console skills"], ["SRS Fundamentals of Robotic Surgery"],
       [module("The robot", "System, team and safety.",
               read("Robotic systems",
                    "A surgeon console, a patient-side cart with instrument arms and a vision cart. Wristed instruments restore degrees of freedom lost in "
                    "laparoscopy, and the 3D view restores depth, but haptic feedback is absent: tissue tension must be judged visually. The bedside "
                    "assistant must be able to undock the robot rapidly in an emergency."),
               explore("Robotic and vision systems", ["robot_surgical_c01", "video_system_a01", "arm_articulated_support_a01"],
                       "Walk round a surgical robot and move its arms."),
               quiz("Check: robotics",
                    q("What is the main sensory limitation of most robotic systems?", "Lack of haptic (force) feedback", "No 3D vision", "No wristed instruments",
                      why="The surgeon must infer force from what they see."))),
        ], prereq=["MIS-202"])

# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
# 3 · GENERAL SURGERY
# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

def procedure_course(code, title, subtitle, specialty, level, outcomes, skills, frameworks, steps_title, steps, check, equipment, sim, prereq):
    course(code, title, subtitle, specialty, level, 5, 5, outcomes, skills, frameworks,
           [module("Principles", "Indications, anatomy and preparation.",
                   read(steps_title, steps, 10),
                   explore("Equipment for this operation", equipment, "Explore the instruments and devices used."),
                   quiz("Check: " + title, *check)),
            module("Procedure", "Practise the operation in the virtual theatre.",
                   {"title": "Procedure lab: " + title, "kind": "VrLab", "minutes": 20, "simulationId": sim,
                    "summary": "Step-by-step procedure with guidance.", "passScore": 70},
                   {"title": "Assessment: " + title, "kind": "VrAssessment", "minutes": 25, "simulationId": sim,
                    "summary": "The procedure unaided, scored by step.", "passScore": 75})],
           prereq=prereq)


procedure_course("GS-301", "Open Appendicectomy", "The classic open operation through a right iliac fossa incision.", "General Surgery", "Intermediate",
                 ["Locate McBurney's point and choose the incision", "Split the muscles in the line of their fibres", "Ligate the mesoappendix and the base"],
                 ["Gridiron incision", "Ligation"], ["ISCP General Surgery"], "Steps of open appendicectomy",
                 "A transverse (Lanz) or oblique incision centred on McBurney's point, a third of the way from the anterior superior iliac spine to the umbilicus. "
                 "Split external oblique, internal oblique and transversus in the line of their fibres, open the peritoneum, deliver the caecum and follow the "
                 "taeniae to the appendix base. Ligate the mesoappendix, crush and ligate the base, divide, then close in layers.",
                 [q("McBurney's point lies…", "One third of the way from the ASIS to the umbilicus", "Midway between pubis and umbilicus", "At the umbilicus",
                    why="It marks the usual position of the appendix base."),
                  q("How is the appendix found when the caecum is delivered?", "Follow the taeniae coli to their convergence", "Palpate for the ileocaecal valve",
                    "Look for the most inflamed loop", why="The three taeniae converge on the appendix base.")],
                 ["instrument_scalpel_handle_a01", "instrument_artery_forceps_b01", "instrument_retractor_a01", "instrument_scissors_a01", "needle_surgical_a01"],
                 "open-appendicectomy", ["SF-103"])

procedure_course("GS-302", "Inguinal Hernia Repair", "Anatomy of the groin and the Lichtenstein tension-free mesh repair.", "General Surgery", "Intermediate",
                 ["Describe the inguinal canal and Hesselbach's triangle", "Distinguish direct and indirect hernias", "Perform a tension-free mesh repair"],
                 ["Groin anatomy", "Mesh repair"], ["ISCP General Surgery", "HerniaSurge guidelines"], "The Lichtenstein repair",
                 "Indirect hernias pass through the deep ring lateral to the inferior epigastric vessels; direct hernias bulge through Hesselbach's triangle "
                 "medial to them. Open the external oblique aponeurosis, protect the ilioinguinal nerve, mobilise the cord, reduce or ligate the sac, then "
                 "lay a mesh on the posterior wall: sutured to the inguinal ligament inferiorly and the conjoint tendon medially, slit around the cord.",
                 [q("An indirect inguinal hernia lies…", "Lateral to the inferior epigastric vessels", "Medial to the inferior epigastric vessels|That is direct.",
                    "Below the inguinal ligament|That is a femoral hernia.", why="Indirect hernias follow the deep ring, lateral to the vessels.")],
                 ["instrument_scalpel_handle_a01", "instrument_selfretaining_retractor_a01", "instrument_artery_forceps_curved_b01", "instrument_scissors_a01"],
                 "inguinal-hernia", ["SF-103"])

procedure_course("GS-303", "Laparotomy: Opening and Closing", "Midline access to the abdomen and a closure that does not fail.", "General Surgery", "Intermediate",
                 ["Open the midline safely, avoiding the bowel", "Explore the abdomen systematically", "Close with a small-bites technique"],
                 ["Midline laparotomy", "Mass closure"], ["ISCP General Surgery", "EHS abdominal wall closure guidelines"], "Opening and closing",
                 "Incise skin and linea alba in the midline, open the peritoneum between clips away from the bowel. Explore systematically. Close the fascia "
                 "with a continuous slowly absorbable monofilament using small bites (5 mm from the edge, 5 mm apart), aiming for a suture-to-wound length "
                 "ratio of at least 4:1 to reduce incisional hernia.",
                 [q("The recommended suture length to wound length ratio for midline closure is at least…", "4:1", "1:1", "2:1", why="A 4:1 ratio with small bites reduces incisional hernia.")],
                 ["instrument_scalpel_handle_a01", "instrument_selfretaining_retractor_a01", "instrument_suction_a01", "needle_surgical_a01", "unit_electrosurgical_a01"],
                 "laparotomy", ["SF-103"])

procedure_course("GS-304", "Bowel Anastomosis", "Hand-sewn and stapled anastomoses and how they fail.", "General Surgery", "Advanced",
                 ["List the requirements for a healthy anastomosis", "Perform a hand-sewn single-layer anastomosis", "Use linear and circular staplers"],
                 ["Hand-sewn anastomosis", "Stapled anastomosis"], ["ISCP General Surgery"], "Principles of anastomosis",
                 "A good anastomosis has a good blood supply, no tension, healthy ends and accurate apposition. Single-layer extramucosal interrupted or "
                 "continuous sutures are standard for hand-sewn joins. Circular staplers create end-to-end colorectal joins; check both doughnuts are complete. "
                 "Leaks typically present on days 3–7 with tachycardia, fever, pain or failure to progress.",
                 [q("After a circular stapled anastomosis you must check…", "Both tissue doughnuts are complete", "The stapler is empty", "The bowel is distended",
                    why="Incomplete doughnuts suggest a defect in the staple line.")],
                 ["stapler_surgical_a01", "instrument_clamp_a01", "instrument_forceps_a01", "needle_surgical_a01"],
                 "bowel-anastomosis", ["GS-303"])

procedure_course("GS-305", "Skin Lesions and Abscesses", "Excision biopsy, incision and drainage under local anaesthetic.", "General Surgery", "Foundation",
                 ["Plan an elliptical excision along skin tension lines", "Drain an abscess safely", "Calculate and give local anaesthetic"],
                 ["Elliptical excision", "Incision and drainage", "Local anaesthesia"], ["ISCP Core Surgical Training"], "Excision and drainage",
                 "Plan an ellipse three times as long as it is wide, along relaxed skin tension lines, with margins appropriate to the suspected diagnosis. "
                 "Abscesses are drained by incision over the fluctuant point, breaking down loculi and leaving the cavity to drain; send pus for culture.",
                 [q("An excision ellipse is usually about how long relative to its width?", "Three times", "Equal", "Ten times", why="A 3:1 ellipse closes without dog-ears.")],
                 ["instrument_scalpel_handle_a01", "instrument_forceps_a01", "syringe_a01", "needle_surgical_a01", "bandage_medical_a01", "medical_sample_bag_a01"],
                 "minor-ops", ["SF-103"])

# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
# 4 · TRAUMA AND EMERGENCY SURGERY
# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

procedure_course("TR-401", "Primary Survey and Haemorrhage Control", "Catastrophic bleeding, airway, breathing, circulation in the injured patient.", "Trauma & Emergency", "Intermediate",
                 ["Run a <C>ABCDE primary survey", "Control external haemorrhage with pressure, packing and tourniquets", "Activate a major haemorrhage protocol"],
                 ["Primary survey", "Tourniquet", "Wound packing"], ["Trauma team training curricula"], "The primary survey",
                 "Catastrophic external haemorrhage first, then airway with cervical spine control, breathing, circulation, disability and exposure. Direct "
                 "pressure, wound packing and a proximal tourniquet control limb bleeding; record the tourniquet time. Give blood products early in a balanced "
                 "ratio and tranexamic acid within 3 hours of injury.",
                 [q("Tranexamic acid in major trauma should be given…", "Within 3 hours of injury", "Only after surgery", "After 6 hours", why="Benefit is lost, and harm possible, if given late.")],
                 ["monitor_patient_a01", "pump_infusion_a01", "blood_bag_a01", "warmer_infusion_a01", "bandage_medical_a03", "trolley_emergency_a01"],
                 "trauma-bay", ["SF-101"])

procedure_course("TR-402", "Chest Drain Insertion", "Safe-triangle anatomy and an open technique.", "Trauma & Emergency", "Intermediate",
                 ["Identify the safe triangle", "Insert a chest drain by blunt dissection", "Connect and secure an underwater seal"],
                 ["Chest drain"], ["BTS pleural guidelines"], "Inserting a chest drain",
                 "The safe triangle: anterior border of latissimus dorsi, lateral border of pectoralis major, a line at the level of the nipple (5th intercostal "
                 "space), apex below the axilla. Infiltrate local anaesthetic, incise, bluntly dissect over the top of the rib (the neurovascular bundle runs "
                 "under it), sweep with a finger, insert the drain, connect to an underwater seal and secure.",
                 [q("Why is dissection done over the upper border of the rib?", "The neurovascular bundle runs under each rib", "The pleura is thinner there", "It hurts less",
                    why="Intercostal vessels and nerve sit in the groove under the rib.")],
                 ["drainage_a01", "drainage_pumpSystem_a01", "instrument_artery_forceps_curved_b01", "instrument_scalpel_handle_a01", "syringe_a02"],
                 "chest-drain", ["SF-103"])

procedure_course("TR-403", "Point-of-Care Ultrasound in Trauma (eFAST)", "Find free fluid and pneumothorax at the bedside.", "Trauma & Emergency", "Intermediate",
                 ["Obtain the four FAST windows and the lung views", "Recognise free fluid and absent lung sliding"],
                 ["eFAST"], ["Emergency ultrasound curricula"], "The eFAST scan",
                 "Four abdominal windows: right upper quadrant (hepatorenal space), left upper quadrant (splenorenal), pelvis and subxiphoid pericardial. Free "
                 "fluid appears black (anechoic). The extended scan adds anterior chest views: absent lung sliding suggests pneumothorax.",
                 [q("Free fluid on FAST appears…", "Anechoic (black)", "Bright white", "Speckled grey", why="Fluid does not reflect ultrasound.")],
                 ["ultrasound_a01", "ultrasound_b01", "monitor_patient_a01"], "efast", ["SF-107"])

procedure_course("TR-404", "Vascular Access", "Peripheral, central and intraosseous access.", "Trauma & Emergency", "Foundation",
                 ["Cannulate a peripheral vein using vein-finding aids", "Describe ultrasound-guided central line insertion with Seldinger technique", "Know when to use intraosseous access"],
                 ["Cannulation", "Seldinger technique"], ["ISCP Core Surgical Training"], "Getting access",
                 "Choose the largest vein you can see or feel; vein finders and ultrasound help in difficult patients. Central lines are placed with "
                 "ultrasound guidance and full asepsis by the Seldinger technique: needle, guidewire (never let go of it), dilator, catheter. Intraosseous "
                 "access is fast when veins fail in an emergency.",
                 [q("During Seldinger insertion, the operator must…", "Keep hold of the guidewire at all times", "Remove the wire before dilating", "Use the dilator first",
                    why="A lost guidewire can embolise.")],
                 ["device_vein_finder_a01", "catheter_system_a01", "syringe_a03", "IV_pole_a01", "pump_infusion_a01", "ultrasound_b01"],
                 "vascular-access", ["SF-101"])

# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
# 5 · SPECIALTIES
# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

procedure_course("OR-501", "Orthopaedic Principles and Bone Instruments", "Fracture principles, fixation and the instruments that work on bone.", "Orthopaedics", "Intermediate",
                 ["Describe a fracture and the principles of reduction and fixation", "Name and use bone instruments", "Apply a tourniquet safely"],
                 ["Fracture description", "Bone instruments"], ["ISCP Trauma & Orthopaedics"], "Fractures and fixation",
                 "Describe a fracture by bone, location, pattern, displacement and soft-tissue state. Treatment: reduce, hold, rehabilitate. Fixation may be "
                 "relative (casts, nails, external fixators) or absolute (compression plating). Bone is cut and shaped with saws, osteotomes, rongeurs, "
                 "rasps (raspatories) and drills; tourniquet time is recorded and kept as short as possible.",
                 [q("Absolute stability of a fracture is achieved by…", "Compression across the fracture (e.g. lag screw and plate)", "A cast", "An intramedullary nail",
                    why="Compression gives primary bone healing; casts and nails give relative stability.")],
                 ["instrument_bone_rongeurs_b01", "instrument_bone_rongeurs_angled_b01", "instrument_bone_clamp_b01", "instrument_bone_forceps_b01", "instrument_bone_hook_b01",
                  "instrument_bone_lever_a01", "instrument_bone_mallet_b01", "instrument_hand_drill_b01", "instrument_handsaw_a01", "instrument_raspatory_a01",
                  "instrument_tamper_a01", "instrument_caliper_a01", "instrument_wire_cutters_b01", "tool_power_surgical_b01"],
                 "ortho-fixation", ["SF-102"])

procedure_course("VS-502", "Vascular Surgery Basics", "Vascular anastomosis and varicose vein surgery.", "Vascular Surgery", "Advanced",
                 ["Gain proximal and distal control of a vessel", "Perform an end-to-side anastomosis", "Describe stripping of the great saphenous vein"],
                 ["Vascular control", "Vascular anastomosis"], ["ISCP Vascular Surgery"], "Vascular principles",
                 "Control above and below before opening a vessel. Use fine non-absorbable monofilament (polypropylene), passing the needle inside-out on "
                 "the artery so the intima is tacked down. Endovenous ablation has largely replaced stripping, but stripping remains an option for selected "
                 "patients.", [q("On the arterial side of an anastomosis the needle passes…", "From inside to outside", "From outside to inside", "Either way",
                    why="Inside-out tacks the intima and prevents a dissection flap.")],
                 ["instrument_stripper_a01", "instrument_clamp_a01", "instrument_dilator_a01", "needle_surgical_a01", "instrument_forceps_a01"],
                 "vascular-anastomosis", ["SF-103"])

procedure_course("PL-503", "Plastic Surgery: Skin Grafts", "Split and full thickness grafts and the dermatome.", "Plastic Surgery", "Intermediate",
                 ["Choose between split and full thickness grafts", "Harvest a split skin graft", "Care for the graft and donor site"],
                 ["Skin grafting"], ["ISCP Plastic Surgery"], "Skin grafts",
                 "Split-thickness grafts include epidermis and part of the dermis, take readily and can be meshed to cover large areas, but contract. "
                 "Full-thickness grafts give a better colour and less contraction for the face and hands. Grafts need a vascular bed, immobility and no "
                 "collections underneath to take.",
                 [q("Which graft contracts more after healing?", "Split thickness", "Full thickness", "They contract equally", why="Less dermis means more secondary contraction.")],
                 ["instrument_skingrafting_knife_a01", "instrument_spatula_a01", "instrument_forceps_a01", "bandage_medical_a04"],
                 "skin-graft", ["SF-103"])

procedure_course("EN-504", "ENT Examination and Emergencies", "Otoscopy, epistaxis and the airway.", "ENT", "Foundation",
                 ["Examine the ear with an otoscope", "Manage anterior epistaxis", "Describe emergency front-of-neck access"],
                 ["Otoscopy", "Nasal packing"], ["ISCP ENT"], "ENT essentials",
                 "Pull the pinna up and back in adults to straighten the canal for otoscopy. Most epistaxis is anterior (Little's area): sit forward, firm "
                 "pressure on the soft nose for 15 minutes, then cautery or packing. In a can't-intubate, can't-oxygenate emergency, scalpel-bougie-tube "
                 "cricothyroidotomy is the standard front-of-neck access.",
                 [q("Most nosebleeds arise from…", "Little's area on the anterior septum", "The posterior nasal cavity", "The inferior turbinate",
                    why="Kiesselbach's plexus in Little's area.")],
                 ["device_otoscope_a01", "instrument_nasal_forceps_b01", "headlight_surgical_a01", "instrument_mirror_a01", "instrument_spoon_a01"],
                 "ent-exam", ["SF-101"])

procedure_course("UR-505", "Urinary Catheterisation and Endourology", "Aseptic catheterisation and the basics of cystoscopy.", "Urology", "Foundation",
                 ["Catheterise a male and a female patient aseptically", "Recognise and manage a failed catheterisation", "Describe flexible cystoscopy"],
                 ["Catheterisation", "Cystoscopy"], ["ISCP Urology"], "Catheterisation",
                 "Aseptic technique, lubricating local anaesthetic gel, and in men the penis held upright to straighten the urethra. Inflate the balloon "
                 "only once urine flows; never inflate in the urethra. Document the residual volume. Failed or traumatic catheterisation needs a urology review.",
                 [q("When should the catheter balloon be inflated?", "Only after urine drains", "As soon as resistance is felt", "Before insertion",
                    why="Inflating in the urethra causes serious trauma.")],
                 ["catheter_system_a01", "drainage_a01", "device_catheter_positioning_system_a01", "device_endoscope_a01", "box_glove_a01"],
                 "catheterisation", ["SF-101"])

procedure_course("CT-506", "Cardiothoracic Surgery Basics", "Sternotomy, bypass and the post-operative chest.", "Cardiothoracic Surgery", "Advanced",
                 ["Describe median sternotomy and its closure", "Explain the cardiopulmonary bypass circuit", "Recognise tamponade after cardiac surgery"],
                 ["Sternotomy", "Bypass circuit"], ["ISCP Cardiothoracic Surgery"], "The heart operation",
                 "Median sternotomy divides the sternum in the midline with a saw and is closed with steel wires. Cardiopulmonary bypass drains venous blood, "
                 "oxygenates it and returns it to the aorta while the heart is arrested with cardioplegia. After surgery, falling blood pressure with rising "
                 "venous pressure and falling drain output suggests tamponade.",
                 [q("Low BP, high CVP and a sudden fall in drain output after cardiac surgery suggest…", "Cardiac tamponade", "Hypovolaemia", "Normal recovery",
                    why="Blood is collecting around the heart instead of draining.")],
                 ["lung_ventilation_artificial_a01", "monitor_patient_a01", "pump_infusion_a01", "drainage_pumpSystem_a01", "defibrillator_a01"],
                 "cardiac", ["SF-107"])

procedure_course("NS-507", "Neurosurgery Basics: Burr Holes", "Raised intracranial pressure and emergency decompression.", "Neurosurgery", "Advanced",
                 ["Recognise an extradural haematoma", "Describe burr-hole placement and technique"],
                 ["Burr hole"], ["ISCP Neurosurgery"], "Burr holes",
                 "An extradural haematoma classically follows temporal trauma with a lucid interval; CT shows a lens-shaped collection. Definitive "
                 "treatment is craniotomy by a neurosurgeon; emergency burr holes are a last resort where transfer is impossible.",
                 [q("An extradural haematoma on CT is typically…", "Biconvex (lens-shaped)", "Crescent-shaped|That is subdural.", "Diffuse", why="It is limited by the suture lines.")],
                 ["instrument_hand_drill_b01", "instrument_punche_a01", "instrument_raspatory_a01", "instrument_suction_a01"], "burr-hole", ["SF-102"])

procedure_course("OG-508", "Caesarean Section Fundamentals", "Steps, team and safety in an operative birth.", "Obstetrics & Gynaecology", "Intermediate",
                 ["Describe the steps of a lower-segment caesarean section", "Explain the urgency categories", "Recognise and treat postpartum haemorrhage"],
                 ["LSCS", "PPH"], ["RCOG curriculum"], "Caesarean section",
                 "Pfannenstiel incision, open the rectus sheath, separate the recti, open the peritoneum, reflect the bladder, transverse incision in the "
                 "lower segment, deliver the baby and placenta, close the uterus in two layers. Postpartum haemorrhage is treated with uterine massage, "
                 "uterotonics, tranexamic acid and escalation.",
                 [q("The uterine incision in a standard caesarean section is…", "Transverse in the lower segment", "Vertical in the upper segment", "Through the fundus",
                    why="The lower segment bleeds less and heals more strongly.")],
                 ["table_surgical_a01", "instrument_retractor_a01", "pump_suction_surgical_a01", "warmer_infusion_a01", "monitor_patient_a01"],
                 "caesarean", ["SF-103"])

# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
# 6 · PERIOPERATIVE PRACTICE (scrub practitioners, nurses, ODPs)
# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

course("PP-601", "The Scrub Practitioner", "Instrument trays, counts, specimens and the sterile supply chain.", "Perioperative Practice", "Foundation", 5, 5,
       ["Set up a back table and Mayo stand", "Run counts and handle specimens correctly", "Track instruments from sterile services to the patient and back"],
       ["Back table", "Counts", "Specimens"], ["AfPP Standards", "ISCP Core Surgical Training"],
       [module("Scrubbing for a case", "Setting up and running the field.",
               read("Back table and Mayo stand",
                    "Open the outer wraps away from you, check sterility indicators and tray lists, then lay out instruments in the order they will be used. "
                    "The Mayo stand holds what the surgeon needs next. Keep sharps in one place and account for every item."),
               explore("Trays, carts and containers",
                       ["trolley_instrument_b01", "trolley_instrument_b05", "cart_medical_utility_a01", "container_system_sterilising_a03",
                        "tray_sterilization_instrument_c02", "instrument_storage_a01", "medical_sample_bag_a01", "container_jar_glass_silver_a01"],
                       "Open drawers, lids and trays and see how a case is set up."),
               lab("Back table and count", "lap-prep", "Agree the opening count at the back table."),
               lab("Closing count", "lap-close", "Find the hidden swab and complete the closing count."),
               quiz("Check: scrub practice",
                    q("A specimen label must include…", "Patient identifiers, specimen site and date", "Only the surgeon's name", "Only the ward",
                      why="Unlabelled or mislabelled specimens are never events waiting to happen."))),
        ], prereq=["SF-101"])

course("PP-602", "Infusion Devices and Patient Equipment", "Infusion pumps, syringe drivers, suction, oxygen and monitoring.", "Perioperative Practice", "Foundation", 3, 3,
       ["Set up an infusion pump and syringe driver safely", "Set up suction and oxygen delivery", "Respond to equipment alarms"],
       ["Infusion pumps", "Suction", "Oxygen therapy"], ["MHRA Infusion device guidance"],
       [module("Devices", "The equipment at every bedside.",
               read("Infusion devices",
                    "Volumetric pumps deliver fluids from bags; syringe drivers deliver small precise volumes. Check the drug, dose, rate and line against "
                    "the prescription, use drug libraries, and never silence an alarm without finding its cause (occlusion, air in line, end of infusion)."),
               explore("Bedside devices",
                       ["pump_infusion_a01", "pump_system_infusion_a01", "warmer_infusion_a01", "syringe_a04", "syringe_insulin_a01", "IV_pole_b01",
                        "pump_suction_surgical_a01", "cannulae_nasal_a01", "mask_nebulization_a01", "device_inhaler_a01", "oximeter_pulse_a01",
                        "pad_vital_a01", "thermometer_fever_a01", "stethoscope_electronic_a01", "blood_glucosemeter_a01"],
                       "Operate the infusion pump door, syringe plunger, nebuliser and more."),
               quiz("Check: devices",
                    q("An infusion pump alarms 'occlusion downstream'. First you…", "Check the line and cannula from pump to patient",
                      "Increase the rate", "Silence and walk away", why="A clamp, kink or blocked cannula is the usual cause."))),
        ])

# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
# QUESTION BANK: extra questions appended to each quiz (keyed by course code and quiz title), so every knowledge check
# has five questions. build() fails if a key no longer matches a quiz.
# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

MORE_QUESTIONS = {
    ("SF-101", "Check: theatre and team"): [
        q("Theatre doors are kept closed during a case mainly to…", "Keep the positive-pressure airflow that pushes contaminated air out",
          "Reduce noise", "Keep the room warm", why="Opening doors disrupts the ventilation that protects the sterile field."),
        q("The circulating practitioner…", "Opens supplies, keeps records and supports the scrubbed team from outside the sterile field",
          "Hands instruments to the surgeon|That is the scrub practitioner.", "Gives the anaesthetic",
          why="The circulator is unscrubbed and links the sterile field to the rest of the theatre."),
        q("If you are unsure whether an item is still sterile, you should…", "Treat it as unsterile and replace it", "Use it if it looks clean",
          "Wipe it with alcohol and use it", why="When in doubt, it is not sterile."),
    ],
    ("SF-101", "Check: the checklist"): [
        q("The three phases of the WHO Surgical Safety Checklist are…", "Sign In, Time Out, Sign Out", "Consent, Incision, Closure",
          "Arrival, Anaesthesia, Recovery", why="Before induction, before incision and before the patient leaves theatre."),
        q("Sign In takes place…", "Before induction of anaesthesia", "After the skin incision", "In the recovery room",
          why="Identity, site, consent, allergies, airway and blood loss risk are confirmed while the patient can still answer."),
        q("At Sign Out the team confirms…", "The procedure, that counts are complete, specimen labels and any equipment problems",
          "Only the patient's name", "The next patient on the list", why="Sign Out closes the loop before the patient leaves."),
    ],
    ("SF-101", "Check: scrubbing"): [
        q("At the first scrub of the day you should also…", "Clean under the nails with a nail pick", "Skip the forearms",
          "Put gloves on before washing", why="Subungual spaces hold most of the hand's bacteria."),
        q("In closed gloving…", "The hands stay inside the gown cuffs while the gloves go on", "The circulator puts your gloves on",
          "Gloves go on before the gown", why="Bare hands never touch the outside of the gown or gloves."),
        q("Your glove is punctured during the case. You…", "Step back and have the glove changed (and the sharp checked)",
          "Carry on until the end", "Wipe the glove with alcohol", why="A puncture breaks the barrier for you and the patient."),
    ],
    ("SF-102", "Check: name the family"): [
        q("Toothed dissecting forceps are best for…", "Skin and fascia", "Bowel and vessels|Teeth crush and tear delicate tissue.", "Nerves",
          why="Teeth grip tough tissue securely; use non-toothed forceps on delicate tissue."),
        q("Artery forceps (haemostats) are used to…", "Clamp bleeding vessels before tying or diathermy", "Cut sutures", "Hold the needle while suturing",
          why="They crush and hold a vessel so it can be ligated or coagulated."),
        q("Compared with Metzenbaum scissors, Mayo scissors are…", "Heavier, for tough tissue and sutures",
          "Finer, for delicate dissection|That describes Metzenbaum.", "Used only for dressings",
          why="Metzenbaum for fine dissection, Mayo for heavy tissue."),
    ],
    ("SF-102", "Check: handling"): [
        q("Sharps are passed in theatre…", "Through a neutral zone such as a kidney dish, not hand to hand", "Hand to hand, point first",
          "Dropped onto the Mayo stand", why="A neutral zone prevents sharps injuries to the team."),
        q("A needle holder grips a curved needle…", "About a third of the way back from the swaged end",
          "At the very tip|It will slip and blunt the point.", "On the swage|The swage is the weakest part.",
          why="That point gives control without bending the needle."),
        q("A pen grip on the scalpel suits…", "Short, fine incisions with a No. 15 blade",
          "Long skin incisions with a No. 10 blade|Use the table-knife grip for those.", "Cutting sutures",
          why="Fine control for small incisions; a table-knife grip for long ones."),
        q("A needle is found loose on the floor. You…", "Pick it up with an instrument, put it in the sharps bin and reconcile the count",
          "Leave it for the cleaners", "Pick it up with your fingers", why="Every sharp must be accounted for, safely."),
    ],
    ("SF-103", "Check: materials"): [
        q("A round-bodied (taper) needle is chosen for…", "Bowel, vessels and other soft tissue that tears easily", "Skin", "Tough fascia only",
          why="It parts tissue without cutting it."),
        q("Which suture is absorbable?", "Polyglactin (Vicryl)", "Polypropylene (Prolene)", "Nylon", why="Polyglactin is absorbed by hydrolysis in weeks."),
        q("Interrupted sutures on the face are usually removed after…", "3–5 days", "10–14 days|That suits the back or limbs.", "21 days",
          why="The face heals fast; early removal avoids track marks."),
    ],
    ("SF-104", "Check: SSI"): [
        q("For most operations, skin is prepared with…", "Alcohol-based chlorhexidine", "Soap and water",
          "Aqueous iodine in every case|Alcohol-based chlorhexidine is preferred unless contraindicated.",
          why="Alcohol-based chlorhexidine gives the lowest SSI rates in most surgery."),
        q("Keeping the patient warm during surgery…", "Reduces surgical site infection", "Increases infection risk", "Has no effect on infection",
          why="Hypothermia impairs immunity and wound perfusion."),
        q("A 'dirty' wound is one…", "With established infection or a perforated viscus", "Through intact skin with no viscus opened|That is clean.",
          "Entering the bowel under control|That is clean-contaminated.", why="Wound class predicts infection risk and guides antibiotics."),
    ],
    ("SF-105", "Check: consent and safety"): [
        q("Consent should be taken by…", "Someone able to do the procedure, or trained to explain it and its risks", "Any member of staff",
          "The patient's relative", why="The person consenting must be able to answer the patient's questions."),
        q("An adult lacks capacity for an urgent operation and has no advance decision. The team…", "Acts in their best interests and documents why",
          "Waits for a relative to sign", "Cancels the operation", why="Relatives are consulted but cannot consent for an adult in UK law."),
        q("Wrong-site surgery is prevented by…", "Marking the site with the patient awake and confirming it at the Time Out",
          "Marking after draping", "Relying on the theatre list", why="Marking and Time Out together catch site errors."),
    ],
    ("SF-106", "Check: energy"): [
        q("In monopolar diathermy the current returns through…", "The patient return electrode (plate)",
          "The other jaw of the forceps|That is bipolar.", "The operating table", why="Current passes through the patient to the plate."),
        q("The return plate goes on…", "Clean, dry, well-perfused muscle such as the thigh, away from metal implants", "A bony prominence",
          "Hairy skin without clipping", why="Good contact over a large area prevents plate burns."),
        q("Surgical smoke (plume) should be…", "Removed with smoke evacuation", "Breathed in, it is harmless", "Cleared by opening the doors",
          why="Plume contains toxic chemicals and particles."),
    ],
    ("SF-107", "Check: monitoring and drugs"): [
        q("Normal end-tidal CO2 is about…", "4.5–6 kPa (35–45 mmHg)", "10–12 kPa", "1–2 kPa", why="A sudden change needs an explanation."),
        q("Early signs of local anaesthetic toxicity include…", "Perioral tingling, tinnitus and confusion", "A rash only", "Pinpoint pupils",
          why="CNS signs come first, then seizures and cardiovascular collapse."),
        q("Severe local anaesthetic toxicity is treated with…", "Intravenous 20% lipid emulsion", "Naloxone", "Flumazenil",
          why="Lipid emulsion with full resuscitation; naloxone and flumazenil reverse other drugs."),
    ],
    ("SF-108", "Check: NOTSS"): [
        q("The four NOTSS categories are situation awareness, decision making, communication and teamwork, and…", "Leadership", "Dexterity", "Speed",
          why="Non-technical skills complement technical skill."),
        q("Situation awareness means gathering information, understanding it and…", "Projecting what will happen next", "Writing it down", "Delegating it",
          why="Anticipation lets the team act before problems escalate."),
        q("A junior sees a safety problem while the consultant is busy. Best is to…", "Speak up clearly, e.g. 'I'm concerned that…'", "Stay silent",
          "Mention it after the case", why="Graded assertiveness: concern, uncomfortable, safety issue."),
        q("A team brief before the list…", "Improves teamwork and catches problems early", "Wastes theatre time", "Is only for the surgeon",
          why="Briefs and debriefs reduce errors and delays."),
    ],
    ("MIS-201", "Check: the tower"): [
        q("CO2 is used for pneumoperitoneum because it is…", "Non-flammable and quickly absorbed and breathed out", "The cheapest gas",
          "Better for image colour", why="It is safe with diathermy and clears quickly."),
        q("Before the laparoscope goes in you should…", "White-balance and focus the camera and warm or defog the lens",
          "Rest the lit scope on the drapes|The light cable tip can burn drapes and skin.", "Nothing: the tower does it",
          why="A clear, true-colour image from the start."),
        q("During insufflation the end-tidal CO2 falls suddenly with hypotension. Think…", "Gas embolism", "Light anaesthesia", "A normal response",
          why="Stop insufflating, release the pneumoperitoneum and resuscitate."),
        q("Working ports are placed so the instruments meet the target at about…", "60°", "180°", "10°",
          why="Triangulation gives the best ergonomics."),
    ],
    ("MIS-201", "Check: diagnostic laparoscopy"): [
        q("Endometriosis is confirmed by…", "Diagnostic laparoscopy", "A blood test",
          "Ultrasound alone|Imaging rules out other causes but cannot confirm it.", why="Only seeing (and biopsying) the lesions confirms it."),
        q("The port incisions in a diagnostic laparoscopy are typically about…", "5 mm", "5 cm", "15 cm", why="Two or three small ports: camera plus instruments."),
        q("Shoulder-tip pain for 2–3 days after laparoscopy is caused by…", "Residual CO2 irritating the diaphragm", "Arm positioning on the table",
          "Wound infection", why="Diaphragmatic irritation is referred to the shoulder tip (C3–5)."),
        q("Endometriosis found at diagnostic laparoscopy can be…", "Excised or ablated in the same operation",
          "Only biopsied; treatment needs a second operation", "Left alone always", why="Diagnose and treat at one sitting where safe."),
        q("Port sites are commonly closed with…", "Absorbable subcuticular sutures", "Steel wires", "Staples left for six weeks",
          why="They dissolve and need no removal."),
    ],
    ("MIS-203", "Check: CVS"): [
        q("The hepatocystic triangle is bounded by the cystic duct, the common hepatic duct and…", "The inferior edge of the liver", "The duodenum",
          "The portal vein", why="This is the area cleared for the critical view."),
        q("The most feared complication of cholecystectomy is…", "Bile duct injury", "A port-site hernia", "Shoulder-tip pain",
          why="The critical view of safety exists to prevent it."),
        q("The gallbladder is removed…", "In a retrieval bag", "Through the port without protection", "It is left in place",
          why="A bag avoids spilling bile and stones into the wound and abdomen."),
    ],
    ("MIS-205", "Check: robotics"): [
        q("In major bleeding the bedside team must be able to…", "Undock the robot quickly and convert", "Wait for the console surgeon to finish",
          "Switch off the vision cart", why="Rapid undocking is a rehearsed emergency drill."),
        q("Wristed robotic instruments add…", "Extra degrees of freedom at the tip", "Force feedback|Most systems have none.", "Lower cost",
          why="They restore wrist movement lost with straight instruments."),
        q("Before docking, the patient must be…", "Positioned and secured: the table usually cannot move once docked", "Left unsecured", "Awake",
          why="Moving the table with instruments inside can injure the patient."),
        q("Depth perception at the console comes from…", "A stereoscopic 3D endoscope", "Haptic feedback", "A larger monitor",
          why="Two cameras give a true 3D image."),
    ],
    ("GS-301", "Check: Open Appendicectomy"): [
        q("In a gridiron incision the abdominal wall muscles are…", "Split in the line of their fibres", "Cut across", "Left intact",
          why="Splitting heals strongly and reduces hernia."),
        q("A perforated appendix with generalised peritonitis needs…", "Resuscitation, antibiotics and urgent surgery", "Oral antibiotics and discharge",
          "Observation only", why="This is a surgical emergency."),
        q("The appendicular artery is a branch of…", "The ileocolic artery", "The right colic artery", "The inferior mesenteric artery",
          why="It runs in the mesoappendix, which is ligated."),
    ],
    ("GS-302", "Check: Inguinal Hernia Repair"): [
        q("The ilioinguinal nerve should be…", "Identified and protected", "Divided routinely", "Ignored",
          why="Nerve injury causes chronic groin pain."),
        q("Hesselbach's triangle: inferior epigastric vessels, lateral border of rectus and…", "The inguinal ligament", "The umbilicus", "The iliac crest",
          why="Direct hernias bulge through it."),
        q("In a Lichtenstein repair the mesh is sutured inferiorly to…", "The inguinal ligament", "The skin", "The femoral vein",
          why="Medially the conjoint tendon, inferiorly the shelving edge of the inguinal ligament."),
        q("A tender, irreducible groin hernia with vomiting suggests…", "Obstruction or strangulation: urgent surgery", "A simple reducible hernia",
          "Normal findings", why="Strangulated bowel can die within hours."),
    ],
    ("GS-303", "Check: Laparotomy: Opening and Closing"): [
        q("'Small bites' closure places bites about…", "5 mm from the edge and 5 mm apart", "1–2 cm from the edge", "Through the skin only",
          why="Small bites lower incisional hernia rates."),
        q("A midline laparotomy goes through…", "The linea alba", "The rectus muscle belly", "The semilunar line", why="The linea alba is relatively avascular."),
        q("The peritoneum is opened…", "Between clips, lifted away from the bowel", "With a deep stab incision", "With diathermy at full power",
          why="Lifting it protects the bowel beneath."),
        q("Mass closure of the fascia uses…", "A continuous slowly absorbable monofilament", "Interrupted fast-absorbing braided sutures", "Silk",
          why="It holds for the months the fascia needs to heal."),
    ],
    ("GS-304", "Check: Bowel Anastomosis"): [
        q("A healthy anastomosis needs…", "A good blood supply and no tension", "Sutures pulled tight to strangle the edges|Over-tight sutures cause ischaemia.",
          "Distended bowel", why="Perfusion and freedom from tension come first."),
        q("An anastomotic leak most often presents…", "Between days 3 and 7", "Within the first hour", "After six weeks",
          why="Watch the early post-operative days closely."),
        q("Signs of an anastomotic leak include…", "Tachycardia, fever, abdominal pain or failure to progress", "Bradycardia and hunger",
          "Nothing: leaks are always silent", why="An unexplained tachycardia is a leak until proven otherwise."),
        q("A single-layer hand-sewn anastomosis is usually…", "Extramucosal", "Through the mucosa only", "Glued",
          why="Extramucosal sutures hold the strong submucosa and invert the mucosa."),
    ],
    ("GS-305", "Check: Skin Lesions and Abscesses"): [
        q("An excision ellipse is orientated…", "Along relaxed skin tension lines", "Across them", "In any direction",
          why="Along the lines gives the neatest scar."),
        q("The maximum dose of lidocaine with adrenaline is about…", "7 mg/kg", "1 mg/kg", "20 mg/kg",
          why="3 mg/kg plain, 7 mg/kg with adrenaline."),
        q("1% lidocaine contains…", "10 mg per ml", "1 mg per ml", "100 mg per ml", why="1% = 1 g in 100 ml = 10 mg/ml."),
        q("Pus from a drained abscess is sent…", "For microscopy, culture and sensitivity", "Nowhere: discard it", "For histology only",
          why="It guides antibiotics if they are needed."),
    ],
    ("TR-401", "Check: Primary Survey and Haemorrhage Control"): [
        q("In <C>ABCDE, <C> stands for…", "Catastrophic external haemorrhage", "Cervical spine|That goes with the airway.", "Circulation",
          why="Stop the bleeding that will kill first."),
        q("When a tourniquet is applied you must record…", "The time it went on", "The patient's weight", "Nothing",
          why="Tourniquet time guides surgery and limb survival."),
        q("A major haemorrhage protocol gives…", "Blood products early, in a balanced ratio of red cells, plasma and platelets", "Litres of crystalloid first",
          "Platelets only", why="Balanced resuscitation corrects coagulopathy."),
        q("Before bleeding is controlled, fluids are titrated to…", "A palpable radial pulse (permissive hypotension)", "A normal blood pressure at all costs",
          "No fluids at all", why="Raising pressure too far can dislodge clot (not for head injury)."),
    ],
    ("TR-402", "Check: Chest Drain Insertion"): [
        q("The lower border of the safe triangle is at about…", "The 5th intercostal space (nipple level)", "The 2nd intercostal space", "The 10th rib",
          why="Lower and the drain risks the diaphragm, liver or spleen."),
        q("A chest drain is connected to…", "An underwater seal kept below the level of the chest", "A bag held above the patient",
          "Full wall suction every time", why="The seal lets air out and keeps it from coming back."),
        q("The water in the drain tubing swings with breathing. This means…", "The drain is in the pleural space and patent", "It is blocked",
          "There is a large air leak|Bubbling shows an air leak.", why="Swinging follows pleural pressure."),
        q("A sharp trocar to push the drain in is…", "Avoided: use blunt dissection", "Recommended", "Required for small drains",
          why="Trocars cause organ injury."),
    ],
    ("TR-403", "Check: Point-of-Care Ultrasound in Trauma (eFAST)"): [
        q("The right upper quadrant view looks at…", "The hepatorenal space (Morison's pouch)", "The spleen", "The bladder",
          why="A common place for free fluid to collect."),
        q("Absent lung sliding on the anterior chest suggests…", "Pneumothorax", "Normal lung", "Haemoperitoneum",
          why="The pleural layers no longer slide on each other."),
        q("The subxiphoid view looks for…", "Pericardial fluid", "Free fluid in the pelvis", "A pneumothorax", why="Tamponade can kill quickly."),
        q("A negative FAST in an unstable patient…", "Does not exclude bleeding: look elsewhere and repeat", "Excludes abdominal injury",
          "Means the patient can go home", why="FAST misses retroperitoneal and small bleeds."),
    ],
    ("TR-404", "Check: Vascular Access"): [
        q("Flow through a cannula depends most on…", "Its internal diameter (flow rises with the radius to the fourth power)", "Its colour",
          "Nothing: all cannulae flow the same", why="Short and wide is fastest."),
        q("A 14G cannula is…", "Wider than an 18G", "Narrower than a 22G", "The same as a 20G", why="Lower gauge means wider."),
        q("Central venous catheters are placed…", "Under ultrasound guidance with full aseptic technique", "Blind and non-sterile", "Only in theatre",
          why="Ultrasound reduces arterial puncture and pneumothorax."),
        q("Intraosseous access is used…", "When venous access fails in an emergency", "First for routine blood tests", "Never in adults",
          why="It works in adults and children in under a minute."),
    ],
    ("OR-501", "Check: Orthopaedic Principles and Bone Instruments"): [
        q("A fracture description includes…", "Bone, location, pattern, displacement and soft-tissue state", "Only the bone", "Only the patient's age",
          why="A complete description guides treatment."),
        q("Fracture treatment is: reduce, hold and…", "Rehabilitate", "Rest indefinitely", "Remove the bone", why="Function is the goal."),
        q("Tourniquet time should be…", "Recorded and kept as short as possible", "Unlimited", "Not recorded",
          why="Prolonged ischaemia damages muscle and nerve."),
        q("A bone rongeur is used to…", "Bite away small pieces of bone", "Hold needles", "Retract skin", why="Its cupped jaws nibble bone."),
    ],
    ("VS-502", "Check: Vascular Surgery Basics"): [
        q("Before opening an artery you must have…", "Proximal and distal control", "Distal control only", "Nothing",
          why="Control before you cut."),
        q("A vascular anastomosis is sewn with…", "Fine non-absorbable monofilament (polypropylene)", "Braided absorbable suture", "Silk",
          why="It slides through the vessel wall and lasts."),
        q("Most varicose veins are now treated with…", "Endovenous ablation", "Stripping in every case", "Amputation",
          why="Stripping remains an option for selected patients."),
        q("Heparin is given before clamping to…", "Prevent thrombosis in the clamped vessels", "Speed clotting", "Relieve pain",
          why="Stagnant blood clots behind a clamp."),
    ],
    ("PL-503", "Check: Plastic Surgery: Skin Grafts"): [
        q("A graft takes best on…", "A well-vascularised bed", "Bare bone without periosteum|Grafts need a blood supply.",
          "Bare tendon without paratenon", why="The graft survives on the bed until it vascularises."),
        q("Meshing a split-thickness graft…", "Lets it cover a larger area and lets fluid escape", "Makes it thicker", "Stops it taking",
          why="Useful in large burns."),
        q("Full-thickness grafts are preferred for…", "The face and hands", "Large burns", "Bone", why="Better colour match and less contraction."),
        q("A common cause of graft failure is…", "Haematoma or seroma under the graft", "A firm dressing", "A good blood supply",
          why="Fluid lifts the graft off its bed."),
    ],
    ("EN-504", "Check: ENT Examination and Emergencies"): [
        q("For otoscopy in an adult the pinna is pulled…", "Up and back", "Down and back|That is for young children.", "Forward",
          why="It straightens the ear canal."),
        q("First aid for an anterior nosebleed is…", "Sit forward and pinch the soft part of the nose for 15 minutes", "Lie flat with the head tipped back",
          "Ice on the forehead only", why="Leaning forward stops blood being swallowed or inhaled."),
        q("In a can't-intubate, can't-oxygenate emergency the standard front-of-neck access is…", "Scalpel–bougie–tube cricothyroidotomy",
          "A nasal airway", "Waiting for the anaesthetist to retry", why="DAS guidelines: scalpel, bougie, tube."),
        q("The cricothyroid membrane lies between…", "The thyroid and cricoid cartilages", "The hyoid and thyroid cartilage", "Below the cricoid",
          why="It is shallow and easy to feel in most adults."),
    ],
    ("UR-505", "Check: Urinary Catheterisation and Endourology"): [
        q("During male catheterisation the penis is held…", "Upright, to straighten the urethra", "Pointing down", "Any way",
          why="It straightens the urethral curve."),
        q("Local anaesthetic gel is instilled…", "Before insertion, and given a few minutes to work", "After insertion", "Never",
          why="It lubricates and numbs the urethra."),
        q("After catheterisation you document…", "The residual volume", "Nothing", "Only the time", why="It confirms retention and guides treatment."),
        q("You meet resistance and the catheter will not pass. You…", "Stop and ask for urology review", "Push harder", "Inflate the balloon",
          why="Forcing causes false passages."),
    ],
    ("CT-506", "Check: Cardiothoracic Surgery Basics"): [
        q("A median sternotomy is closed with…", "Steel wires", "Skin staples only", "Glue", why="Wires hold the sternum while it heals."),
        q("On cardiopulmonary bypass the heart is stopped with…", "Cardioplegia", "Adrenaline", "Lidocaine",
          why="A cold potassium-rich solution arrests the heart."),
        q("Beck's triad of tamponade is…", "Hypotension, raised JVP and muffled heart sounds",
          "Hypertension, bradycardia and irregular breathing|That is Cushing's triad.", "Fever, rash and joint pain",
          why="The heart cannot fill."),
        q("Tamponade after cardiac surgery is treated by…", "Urgent re-exploration (re-opening the chest)", "Fluids and waiting", "Diuretics",
          why="Release the blood around the heart."),
    ],
    ("NS-507", "Check: Neurosurgery Basics: Burr Holes"): [
        q("A lucid interval after head injury is classic of…", "Extradural haematoma", "Subdural haematoma", "Subarachnoid haemorrhage",
          why="Consciousness returns, then falls as the clot grows."),
        q("An extradural haematoma usually comes from…", "The middle meningeal artery", "Bridging veins|That is subdural.", "A berry aneurysm",
          why="Often a temporal bone fracture tears it."),
        q("Signs of rising intracranial pressure include…", "Falling GCS, a dilating pupil and Cushing's response", "A rising GCS",
          "Small pupils and alertness", why="Act fast: these are late signs."),
        q("Definitive treatment of an extradural haematoma is…", "Craniotomy by a neurosurgeon", "Burr holes in every case", "Observation",
          why="Burr holes are a last resort when transfer is impossible."),
    ],
    ("OG-508", "Check: Caesarean Section Fundamentals"): [
        q("The usual skin incision is…", "Pfannenstiel (transverse, suprapubic)", "Upper midline", "Kocher (right subcostal)",
          why="It heals well with a hidden scar."),
        q("A category 1 caesarean section means…", "Immediate threat to the life of the woman or baby", "Planned, at a time to suit",
          "Maternal request only", why="Aim to deliver as quickly as safely possible."),
        q("First-line drug for postpartum haemorrhage is…", "Oxytocin", "Furosemide", "Insulin", why="It contracts the uterus."),
        q("The bladder is reflected down before the uterine incision to…", "Protect it from injury", "Drain urine", "See the placenta",
          why="The bladder lies over the lower segment."),
    ],
    ("PP-601", "Check: scrub practice"): [
        q("The Mayo stand holds…", "What the surgeon will need next", "Spare stock", "Used swabs", why="The back table holds the rest."),
        q("Counts are done…", "Before the case, before closing a cavity and at skin closure (and on staff changes)", "Only at the end",
          "Only if something is missing", why="Regular counts catch a missing item early."),
        q("If a count is incorrect…", "Tell the surgeon at once, search, and X-ray if not found", "Ignore it", "Correct the sheet",
          why="Retained items are never events."),
        q("Before using a sterile tray, check…", "The sterility indicator and that the wrap is intact and dry", "Its colour", "Nothing",
          why="A damp or torn wrap means it is not sterile."),
    ],
    ("PP-602", "Check: devices"): [
        q("Drug libraries on infusion pumps…", "Set safe dose limits and cut programming errors", "Make infusions faster", "Replace the prescription",
          why="Soft and hard limits catch mistakes."),
        q("A syringe driver is preferred for…", "Small, precise volumes over time", "Fast fluid boluses", "Litres of fluid",
          why="It delivers tiny rates accurately."),
        q("An 'air in line' alarm sounds. You…", "Stop, clear the air following the protocol and check the line before restarting",
          "Silence it and carry on", "Speed up the infusion", why="Air embolism is a preventable harm."),
        q("Before starting an infusion, check…", "Drug, dose, rate, route and patient against the prescription", "Only the bag colour", "Nothing",
          why="Independent checks prevent infusion errors."),
    ],
}


def add_question_bank():
    by_key = {}
    for c in COURSES:
        for m in c["modules"]:
            for l in m["lessons"]:
                if l["kind"] == "Quiz":
                    by_key[(c["code"], l["title"])] = l
    for key, extra in MORE_QUESTIONS.items():
        assert key in by_key, f"question bank key {key} matches no quiz"
        l = by_key[key]
        l["questions"].extend(extra)
        l["summary"] = f"{len(l['questions'])} questions with feedback."
        l["minutes"] = max(l["minutes"], len(l["questions"]) * 2)
    thin = [k for k, l in by_key.items() if len(l["questions"]) < 5]
    assert not thin, f"quizzes with fewer than five questions: {thin}"


# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
# PROGRAMS
# ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

PROGRAMS = [
    ("Surgical Foundations", "Everything a medical student or new trainee needs before their first theatre list.",
     ["SF-101", "SF-102", "SF-103", "SF-104", "SF-105", "SF-106", "SF-107", "SF-108"]),
    ("Minimally Invasive Surgery", "From the laparoscopic tower to a complete cholecystectomy, mapped to FLS.",
     ["MIS-201", "MIS-202", "MIS-203", "MIS-204", "MIS-205"]),
    ("General Surgery Procedures", "The core open operations of general surgical training.",
     ["GS-301", "GS-302", "GS-303", "GS-304", "GS-305"]),
    ("Trauma and Emergency Surgery", "Life-saving procedures for the injured and acutely unwell patient.",
     ["TR-401", "TR-402", "TR-403", "TR-404"]),
    ("Surgical Specialties", "First steps into orthopaedics, vascular, plastics, ENT, urology, cardiothoracic, neurosurgery and obstetrics.",
     ["OR-501", "VS-502", "PL-503", "EN-504", "UR-505", "CT-506", "NS-507", "OG-508"]),
    ("Perioperative Practice", "For scrub practitioners, theatre nurses and ODPs.",
     ["PP-601", "PP-602", "SF-101", "SF-104"]),
]


def build():
    add_question_bank()
    for c in COURSES:
        for mi, m in enumerate(c["modules"]):
            m["id"] = f"{c['id']}-m{mi + 1}"
            for li, l in enumerate(m["lessons"]):
                l["id"] = f"{m['id']}-l{li + 1}"
                for qi, qq in enumerate(l.get("questions", [])):
                    qq["id"] = f"{l['id']}-q{qi + 1}"
        c["modules"] = [m for m in c["modules"] if m["lessons"]]  # drop placeholder modules with no lessons
    ids = {c["id"] for c in COURSES}
    for c in COURSES:
        missing = [p for p in c["prerequisites"] if p not in ids]
        assert not missing, f"{c['code']} has unknown prerequisites {missing}"
    programs = [{"id": slug(t), "title": t, "summary": s, "courseIds": [slug(x) for x in cs]} for t, s, cs in PROGRAMS]
    for p in programs:
        for cid in p["courseIds"]:
            assert cid in ids, f"program {p['id']} lists unknown course {cid}"
    catalog = {"version": VERSION, "programs": programs, "courses": COURSES}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(catalog, f, indent=1, ensure_ascii=False)
    if os.path.isdir(os.path.dirname(API_OUT)):
        with open(API_OUT, "w", encoding="utf-8") as f:
            json.dump(catalog, f, indent=1, ensure_ascii=False)
        print("also written to", os.path.normpath(API_OUT))
    lessons = sum(len(m["lessons"]) for c in COURSES for m in c["modules"])
    sims = sorted({l.get("simulationId") for c in COURSES for m in c["modules"] for l in m["lessons"] if l.get("simulationId")})
    equipment = sorted({e for c in COURSES for m in c["modules"] for l in m["lessons"] for e in l.get("equipment", [])})
    print(f"{len(programs)} programs, {len(COURSES)} courses, {lessons} lessons -> {os.path.normpath(OUT)}")
    print("simulations:", ", ".join(sims))
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "equipment_used.json"), "w") as f:
        json.dump(equipment, f, indent=1)
    print(f"{len(equipment)} distinct SimReady assets referenced")


if __name__ == "__main__":
    build()
