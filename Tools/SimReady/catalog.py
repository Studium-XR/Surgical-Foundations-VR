"""
The NVIDIA SimReady healthcare set used by Surgical Foundations / Studium XR.

Source: the GLB conversion of the SimReady library on Hugging Face (codepawl/simready-glb, CC BY 4.0), which carries
the same assets, part names and hierarchy as the USD originals browsed at huggingface.co/spaces/nvidia/simready-central
at roughly 1/20 of the size (the USDs run 50 MB-1.2 GB each; the whole healthcare set as GLB is about 1.4 GB).

Each entry: asset name -> category. The category decides where the prefab goes in Unity and which rigging rules
apply in Blender (see rig_simready.py).
"""

DATASET = "codepawl/simready-glb"
URL = "https://huggingface.co/datasets/" + DATASET + "/resolve/main/engine/{name}/{name}.glb"

# Prefix -> category. Order matters: the first matching prefix wins.
PREFIXES = [
    ("instrument_", "Instruments"),
    ("needle_", "Instruments"),
    ("stapler_surgical", "Instruments"),
    ("tool_pen_surgical", "Instruments"),
    ("tool_power_surgical", "Instruments"),
    ("syringe", "Consumables"),
    ("bandage_medical", "Consumables"),
    ("blood_bag", "Consumables"),
    ("blood_line", "Consumables"),
    ("cannulae_nasal", "Consumables"),
    ("catheter_system", "Consumables"),
    ("drainage", "Consumables"),
    ("drape_surgical", "Consumables"),
    ("facemask_medical", "Consumables"),
    ("anesthesia_mask", "Consumables"),
    ("mask_nebulization", "Consumables"),
    ("medical_sample_bag", "Consumables"),
    ("medicine_", "Consumables"),
    ("box_glove", "Consumables"),
    ("ppe_gloves_lightDuty", "Consumables"),
    ("container_jar_glass", "Consumables"),
    ("container_system_sterilising", "Sterile"),
    ("tray_sterilization", "Sterile"),
    ("disinfectant", "Sterile"),
    ("cleaning_soapDispenser", "Sterile"),
    ("fixture_soapDispenser", "Sterile"),
    ("sink_surgical", "Sterile"),
    ("fixture_sink_basin", "Sterile"),
    ("sanitary_sink", "Sterile"),
    ("device_", "Devices"),
    ("blood_glucosemeter", "Devices"),
    ("defibrillator", "Devices"),
    ("oximeter", "Devices"),
    ("stethoscope", "Devices"),
    ("thermometer", "Devices"),
    ("headlight_surgical", "Devices"),
    ("ultrasound", "Devices"),
    ("pad_vital", "Devices"),
    ("anesthesia_machine", "Equipment"),
    ("lung_ventilation", "Equipment"),
    ("monitor_patient", "Equipment"),
    ("monitor_surgical", "Equipment"),
    ("pump_infusion", "Equipment"),
    ("pump_suction_surgical", "Equipment"),
    ("pump_system_infusion", "Equipment"),
    ("warmer_infusion", "Equipment"),
    ("unit_electrosurgical", "Equipment"),
    ("laser_system", "Equipment"),
    ("video_system", "Equipment"),
    ("robot_surgical", "Equipment"),
    ("CT_injection", "Imaging"),
    ("Xray_machine", "Imaging"),
    ("IV_pole", "Equipment"),
    ("table_surgical", "Furniture"),
    ("bed_hospital", "Furniture"),
    ("chair_hospital", "Furniture"),
    ("chair_patient_mobile", "Furniture"),
    ("chair_surgical", "Furniture"),
    ("wheelChair", "Furniture"),
    ("screen_hospital", "Furniture"),
    ("lamp_table_bedside", "Furniture"),
    ("facilityFurniture_lockerUnit", "Furniture"),
    ("storage_locker_double_bench", "Furniture"),
    ("clogs_hospital", "Furniture"),
    ("cart_medical_utility", "Carts"),
    ("cart_mobile_", "Carts"),
    ("cart_computer", "Carts"),
    ("cart_equipment_diagnostic", "Carts"),
    ("trolley_instrument", "Carts"),
    ("trolley_emergency", "Carts"),
    ("light_surgical", "Lighting"),
    ("light_head_bed", "Lighting"),
    ("lighting_main_light", "Lighting"),
    ("lighting_main_mount", "Lighting"),
    ("arm_articulated_support", "Architecture"),
    ("pendant_medical", "Architecture"),
    ("unit_head_bed", "Architecture"),
    ("sign_hospital", "Architecture"),
    ("bin_waste", "Architecture"),
    ("fixture_trashBin", "Architecture"),
]


def category(name):
    for prefix, cat in PREFIXES:
        if name.startswith(prefix):
            return cat
    return None
