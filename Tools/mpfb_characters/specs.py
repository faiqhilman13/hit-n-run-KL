"""
Character specs for the MPFB (MakeHuman) cast: stylized adults (about 6.5-7 heads tall), built on
MPFB's CC0 base mesh so anatomy, topology, UVs and the rig come from a proper human, then pushed
toward a friendly stylized look with MPFB's own shape targets (bigger head and eyes, softer jaw).

phenotype: MPFB macro sliders 0..1 (age 0.5 = 25 y, 1.0 = 90 y; height 0.5 = average for the gender).
targets:   MPFB target name -> weight (see data/targets/<group>/<name>.target.gz in the extension).
"""

# Shared by everyone: the stylization that turns MPFB's realistic human into the game's look.
# A head about 12% bigger, eyes about 20% bigger, a little less jaw and nose detail.
STYLIZE = {
    "head-scale-vert-incr": 0.65,
    "head-scale-horiz-incr": 0.65,
    "head-scale-depth-incr": 0.5,
    "l-eye-scale-incr": 0.9,
    "r-eye-scale-incr": 0.9,
    "l-eye-trans-out": 0.10,
    "r-eye-trans-out": 0.10,
    "l-eye-height2-incr": 0.35,           # a touch more open: friendlier, reads at distance
    "r-eye-height2-incr": 0.35,
    "chin-prominent-decr": 0.20,
    "nose-scale-horiz-decr": 0.10,
    "neck-scale-horiz-decr": 0.10,
    "measure-shoulder-dist-decr": 0.10,
    # everyone's clothed: no nipples pushing through shirts and baju
    "nipple-point-decr": 1.0,
    "nipple-size-decr": 1.0,
}

def age(years):
    """MPFB's age slider: 1 y = 0.0, 25 y = 0.5, 90 y = 1.0."""
    if years <= 25:
        return max(0.0, (years - 1) / 48.0)
    return min(1.0, 0.5 + (years - 25) / 130.0)

SPECS = {
    "chr_pakmat": {
        "label": "Pak Mat",
        "phenotype": {
            "gender": 1.0, "age": age(50), "muscle": 0.45, "weight": 0.85, "proportions": 0.55,
            "height": 0.30, "cupsize": 0.5, "firmness": 0.35,
            "race": {"asian": 0.78, "african": 0.12, "caucasian": 0.10},
        },
        "targets": {
            "stomach-pregnant-incr": 0.55,      # the generous belly
            "breast-point-decr": 0.6, "breast-volume-vert-up": 0.3,
            "l-cheek-volume-incr": 0.7, "r-cheek-volume-incr": 0.7,         # round, warm cheeks
            "nose-scale-horiz-incr": 0.5, "nose-point-width-incr": 1.0, "nose-volume-incr": 0.9,
            "nose-scale-vert-decr": 0.3, "nose-flaring-incr": 0.2, "nose-scale-depth-incr": 0.3,   # the big round nose (memorable)
            "mouth-scale-horiz-incr": 0.2, "mouth-angles-up": 1.0,          # a resting smile
            "neck-double-incr": 0.25,
            "head-round": 0.9, "chin-prominent-decr": 0.3, "chin-width-incr": 0.3,   # a round, soft face
            "l-eye-eyefold-down": 0.20, "r-eye-eyefold-down": 0.20,     # a little weary
            "head-age-incr": 0.15,                # older, not lined: the lines read uncanny at toon scale
            "l-eye-bag-incr": 0.1, "r-eye-bag-incr": 0.1,
            "eyebrows-angle-up": 0.35,          # inner brows up: kind, a bit worried, not cross
        },
        "skin": "middleage_asian_male.mhmat",
        "skin_tone": (0.47, 0.265, 0.15),         # warm medium brown (multiplies the skin map)
        "eyebrows": "eyebrow001.mhclo",
        "hair": "short02.mhclo",
        "height_m": 1.66,
        "hair_color": (0.025, 0.02, 0.018),
        "moustache": True,
        # halfway back to the cartoon: bigger head and eyes, the face's realistic micro-forms relaxed away,
        # flatter painted skin, solid brows (Upin & Ipin warmth on the adult body)
        "toon": {"head": 1.42, "eyes": 1.6, "smooth": 20, "skin_detail": 0.04, "solid_brows": True, "no_lashes": True, "blush": 0.22},
        "outfit": [
            {"kind": "sandals"},
            {"kind": "shirt", "name": "batik", "sleeve": 0.5, "pattern": "pakmat"},
            {"kind": "wrap", "name": "sarong", "tint": (0.62, 0.85, 0.7)},
            {"kind": "songkok"},
        ],
    },
}
