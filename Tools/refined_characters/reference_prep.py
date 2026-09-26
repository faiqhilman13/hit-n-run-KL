"""Deterministic Phase 0 reference preparation for the approved family concepts.

Run with Python 3 + Pillow, NumPy, SciPy. No creative image generation is used.
The original sheets are preserved. Crops are masks/measurement aids, not revised art.
"""
from pathlib import Path
import json
import shutil

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT.parents[1] / "docs" / "character-concepts" / "round-01"
CANVAS = (512, 704)
GROUND = 688
AXIS = 256

# All coordinates below are manually read from the approved 1536x1024 sheets.
# Joint centres under clothes are estimates, explicitly recorded in the brief.
ASSETS = {
    "chr_pakmat": dict(name="Pak Mat", prefix="CH_PakMat", file="a-pak-mat-sheet-v1.png", height=1.66,
        top=73, floor=681, axes=[216, 574, 947, 1340], boxes=[(20,68,408,686),(454,68,712,686),(759,68,1133,686),(1164,68,1505,686)],
        head_top=121, chin=261, head_span=[155,278], ear_span=[138,294], hair_span=[147,283],
        headgear_top=73, headgear_span=[158,272], shoulder_span=[121,312], hip_span=[103,332],
        landmarks={"crown":(216,73),"chin":(216,261),"nose_tip":(216,193),"pupil_left":(202,167),"pupil_right":(236,167),
            "ear_left":(148,187),"ear_right":(285,187),"shoulder_left":(128,269),"shoulder_right":(304,269),
            "elbow_left":(93,373),"elbow_right":(341,373),"wrist_left":(58,444),"wrist_right":(374,444),
            "fingertip_left":(59,504),"fingertip_right":(373,505),"shirt_hem":(216,464),"waist":(216,453),
            "sarong_hem":(216,633),"ankle_left":(126,641),"ankle_right":(298,641),"foot_left":(123,674),"foot_right":(310,674)},
        side_landmarks={"crown":(582,73),"forehead":(633,127),"nose_tip":(668,183),"chin":(640,239),"nape":(538,211),
            "shoulder":(548,264),"elbow":(556,376),"wrist":(568,448),"fingertip":(571,507),
            "belly_front":(700,404),"waist_front":(692,451),"waist_back":(464,450),"hem_front":(674,619),"hem_back":(470,619),"heel":(519,674),"toe":(654,674)},
        costume="burnt orange floral batik shirt, dark green checked sarong, black songkok, brown sandals, black moustache",
        silhouette="Tall flat-topped songkok; broad pear-shaped belly; generous rounded cheeks and moustache; continuous calf-length sarong; short exposed ankles and broad sandals.",
        parts="Body/neck/limbs deforming; skin head and ears deforming at head/neck; eyes separate rigid head children; brows and moustache separate head children; songkok separate rigid head child; collar and loose short-sleeve shirt separate deforming layer; sarong separate continuous deforming shell; four fingers and thumb per hand; sandal sole and straps follow foot bones.",
        colors=["#ce915f", "#a74027", "#662629", "#bc8b35", "#244333", "#493527"],
        inference="Pelvis and knees concealed by sarong; infer pelvis z=0.77m and knees z=0.43m. Hat and skull depth taken from profile. Sarong is continuous in the source, not trousers; use skirt bones or broad blended weights. Back pattern placement is illustrative and not registered with the front."),
    "chr_maksom": dict(name="Mak Som", prefix="CH_MakSom", file="b-mak-som-sheet-v1.png", height=1.57,
        top=92, floor=648, axes=[207,578,938,1338], boxes=[(28,86,389,653),(477,86,688,653),(765,86,1111,653),(1172,86,1496,653)],
        head_top=113, chin=237, head_span=[148,267], ear_span=[148,267], hair_span=[133,282],
        headgear_top=92, headgear_span=[133,282], shoulder_span=[118,297], hip_span=[93,320],
        landmarks={"crown":(207,92),"chin":(207,237),"nose_tip":(207,191),"pupil_left":(189,170),"pupil_right":(230,170),
            "shoulder_left":(128,267),"shoulder_right":(287,267),"elbow_left":(91,360),"elbow_right":(327,360),
            "wrist_left":(62,429),"wrist_right":(354,429),"fingertip_left":(58,483),"fingertip_right":(356,483),
            "hijab_tip":(207,331),"waist":(207,397),"tunic_hem":(207,501),"skirt_hem":(207,616),
            "ankle_left":(139,622),"ankle_right":(270,622),"foot_left":(137,643),"foot_right":(273,643)},
        side_landmarks={"crown":(589,91),"forehead":(647,129),"nose_tip":(668,188),"chin":(648,231),"nape":(571,237),
            "shoulder":(563,267),"elbow":(574,355),"wrist":(581,424),"fingertip":(583,483),
            "hijab_back":(489,244),"hijab_front":(676,311),"waist_front":(669,400),"waist_back":(499,400),
            "hem_front":(655,614),"hem_back":(497,614),"heel":(528,642),"toe":(657,642)},
        costume="soft coral floral baju kurung, dusty blue tudung, dark blue sandals",
        silhouette="Soft oval hijab surrounding a round face; draped bib-shaped scarf; full sleeves with natural hand clearance; long layered tunic over continuous ankle-length skirt; compact blue sandals.",
        parts="Complete body/neck/limbs deforming under clothing; face head and ears on head bone; separate eyes and brows on head; hijab shell follows head/neck/chest using blended weights; tunic and sleeve shells separate deforming garment; skirt separate continuous deforming shell; individual fingers and thumb; sandals follow feet.",
        colors=["#cd9169", "#e57879", "#953749", "#e5b865", "#6584ab", "#33435c"],
        inference="Scalp and ears hidden by tudung; infer a regular rounded skull and covered ears. Pelvis and knees hidden by long garments; use the source outer outline as dimensional authority. Tudung rear drape is much longer than its front drape and needs independent shoulder clearance. Floral print is illustrative, not UV correspondence."),
    "chr_along": dict(name="Along", prefix="CH_Along", file="c-along-sheet-v1.png", height=1.64,
        top=68, floor=677, axes=[243,579,911,1291], boxes=[(104,63,383,682),(497,63,681,682),(773,63,1054,682),(1171,63,1414,682)],
        head_top=116, chin=232, head_span=[198,284], ear_span=[181,303], hair_span=[157,325],
        headgear_top=68, headgear_span=[157,325], shoulder_span=[177,307], hip_span=[176,308],
        landmarks={"crown":(243,68),"chin":(243,232),"nose_tip":(243,192),"pupil_left":(229,169),"pupil_right":(266,169),
            "ear_left":(193,193),"ear_right":(292,188),"neck_base":(243,259),"shoulder_left":(189,272),"shoulder_right":(296,272),
            "elbow_left":(163,356),"elbow_right":(321,356),"wrist_left":(137,422),"wrist_right":(349,422),
            "fingertip_left":(134,487),"fingertip_right":(350,487),"waist":(243,432),"crotch":(243,465),
            "shorts_hem":(243,526),"knee_left":(193,549),"knee_right":(292,549),"ankle_left":(192,605),"ankle_right":(292,605),"foot_left":(173,670),"foot_right":(313,670)},
        side_landmarks={"crown":(582,68),"forehead":(639,141),"nose_tip":(663,189),"chin":(635,227),"nape":(566,226),
            "shoulder":(573,269),"elbow":(576,356),"wrist":(582,424),"fingertip":(582,486),"waist_front":(636,428),"waist_back":(538,428),
            "shorts_front":(626,520),"shorts_back":(540,520),"knee":(575,548),"ankle":(568,605),"heel":(530,671),"toe":(661,671)},
        costume="red cream-ribbed T-shirt, navy cargo shorts, cream socks, navy/cream chunky trainers, gold wristband on anatomical right",
        silhouette="Dense round curled hair crown; lanky narrow neck and slim torso; long relaxed forearms; cargo shorts ending above knees; oversized rounded trainers.",
        parts="Complete body with segmented joint loops; separate deforming head and ears; eyes/brows separate head children; hair cap plus authored curl volumes rigid to head; T-shirt shell and collar; shorts shell with cargo pockets; socks; trainer sole/upper/laces follow feet; right wristband follows right forearm; individual fingers and thumbs.",
        colors=["#bf8352", "#29272a", "#a73b31", "#33435c", "#e4b234", "#e8e2d4"],
        inference="Hair curls differ among views; preserve total crown volume and silhouette, not one-to-one curl placement. Back shorts pockets need plausible thickness. Hidden hip joint and pelvis depth inferred within shorts. Along's right wristband appears on image-left in front, image-right in back; preserve anatomical right."),
    "chr_adik": dict(name="Adik", prefix="CH_Adik", file="d-adik-sheet-v1.png", height=1.12,
        top=104, floor=679, axes=[206,565,933,1314], boxes=[(28,99,387,684),(440,99,694,684),(751,99,1112,684),(1132,99,1485,684)],
        head_top=151, chin=329, head_span=[111,300], ear_span=[87,325], hair_span=[38,375],
        headgear_top=104, headgear_span=[96,316], shoulder_span=[137,276], hip_span=[99,311],
        landmarks={"crown":(206,104),"chin":(206,329),"nose_tip":(206,272),"pupil_left":(173,249),"pupil_right":(241,249),
            "ear_left":(100,264),"ear_right":(312,263),"pigtail_left":(60,229),"pigtail_right":(354,229),
            "shoulder_left":(146,357),"shoulder_right":(267,357),"elbow_left":(109,433),"elbow_right":(301,433),
            "wrist_left":(80,477),"wrist_right":(329,477),"fingertip_left":(68,532),"fingertip_right":(341,532),
            "waist":(206,440),"dress_hem":(206,568),"knee_left":(158,591),"knee_right":(259,591),
            "ankle_left":(154,640),"ankle_right":(260,640),"foot_left":(144,673),"foot_right":(266,673)},
        side_landmarks={"crown":(579,108),"forehead":(669,178),"nose_tip":(680,268),"chin":(645,325),"nape":(557,312),
            "shoulder":(561,360),"elbow":(561,440),"wrist":(565,483),"fingertip":(565,535),"waist_front":(636,440),"waist_back":(511,440),
            "hem_front":(670,567),"hem_back":(478,567),"knee":(558,591),"ankle":(559,639),"heel":(524,671),"toe":(647,671)},
        costume="coral red A-line dress with cream rounded collar and cream hem, black twin pigtails with red round ties, navy sandals",
        silhouette="Very large rounded head and wide-set eyes; short outward pigtails; narrow shoulders; gently flared dress with broad cream hem; short rounded calves and substantial sandals.",
        parts="Complete underlying child-proportioned body; separate deforming head and ears; eyes/brows rigid to head; fitted hair cap and fringe with two pigtails and round ties on head/secondary bones; dress shell with separate collar and hem roles; individual fingers and thumbs; sandals bound to foot bones.",
        colors=["#e5a471", "#ec6260", "#efe9dc", "#302b2f", "#303f61", "#be332d"],
        inference="Side hair top is 4 px below front; keep front height and treat this as a sheet discrepancy. Rear dress hem and side flare vary; front width wins, side depth guides volume. Pelvis/crotch hidden by dress; use child body beneath a continuous skirt with cloth bones. Hair ties and sandal straps differ slightly among panels."),
}

# Source-space regions remove ground lines before hole filling, otherwise those
# lines incorrectly close the negative space between legs. The bounds encompass
# the visible footwear; they do not define or smooth its silhouette.
FOOT_BOUNDS = {
    "chr_pakmat": [[(74,175),(259,358)],[(517,657)],[(819,910),(978,1070)],[(1229,1315),(1348,1469)]],
    "chr_maksom": [[(98,180),(232,314)],[(526,659)],[(844,915),(960,1034)],[(1241,1331),(1345,1455)]],
    "chr_along": [[(125,222),(263,361)],[(529,663)],[(800,886),(936,1018)],[(1178,1292),(1321,1399)]],
    "chr_adik": [[(103,185),(225,305)],[(523,649)],[(847,918),(951,1022)],[(1242,1317),(1334,1440)]],
}
# Pale cream fabric resembles the background. These source-space polygons cover
# the clearly visible collar/hem surface and preserve the ORIGINAL pixel colours.
# They are manual analytic segmentation aids, not creative additions or shape edits.
PALE_FABRIC_POLYGONS = {
    0: [[(155,330),(174,333),(207,350),(236,334),(256,332),(256,346),(248,364),(230,369),(208,359),(190,368),(175,367),(160,356)],
        [(103,538),(98,555),(98,564),(123,570),(280,572),(310,566),(308,543)]],
    1: [[(535,327),(551,325),(570,331),(590,342),(600,352),(584,351),(553,338),(534,340),(529,349),(531,335)],
        [(484,540),(479,562),(480,567),(491,571),(651,572),(669,568),(667,543)]],
    2: [[(887,328),(903,325),(961,326),(979,329),(976,339),(960,347),(907,347),(886,337)],
        [(836,540),(832,560),(831,565),(836,568),(918,572),(999,571),(1036,567),(1031,542)]],
    3: [[(1270,334),(1290,331),(1316,347),(1337,333),(1351,339),(1354,350),(1343,369),(1327,371),(1314,359),(1303,370),(1287,366),(1275,353)],
        [(1225,541),(1220,561),(1220,566),(1228,570),(1388,574),(1424,568),(1420,544)]],
}


def clean_mask(rgb, asset, view_index, box, floor):
    a = np.asarray(rgb).astype(np.int16)
    # Grid/background are pale warm neutrals. Dark contour plus chroma identifies art.
    seed = (a.min(axis=2) < 186) | ((a.max(axis=2) - a.min(axis=2)) > 47)
    seed = ndimage.binary_closing(seed, iterations=1)
    floor_start=floor-box[1]-15
    foot_zone=np.zeros(seed.shape[1],dtype=bool)
    for left,right in FOOT_BOUNDS[asset][view_index]:
        foot_zone[max(0,left-box[0]):min(seed.shape[1],right-box[0]+1)]=True
    seed[max(0,floor_start):,~foot_zone]=False
    if asset=="chr_adik":
        fill=Image.new("1",rgb.size)
        dr=ImageDraw.Draw(fill)
        for poly in PALE_FABRIC_POLYGONS[view_index]:
            dr.polygon([(x-box[0],y-box[1]) for x,y in poly],fill=1)
        seed |= np.asarray(fill,dtype=bool)
    labels, n = ndimage.label(seed)
    sizes = np.bincount(labels.ravel())
    keep = np.zeros_like(seed)
    for k in range(1,n+1):
        if sizes[k] > 250:
            keep |= labels == k
    return ndimage.binary_fill_holes(keep)


def silhouette_profile(mask, mpp):
    out = []
    for row in range(mask.shape[0]):
        xx = np.nonzero(mask[row])[0]
        if len(xx):
            # All occupied intervals retained (hand gaps and separated legs are meaningful).
            runs=[]
            for group in np.split(xx, np.where(np.diff(xx)>1)[0]+1):
                runs.append([int(group[0]),int(group[-1])])
            out.append({"row":row,"z_m":round((GROUND-row)*mpp,6),"intervals_px":runs,
                        "min_m":round((int(xx[0])-AXIS)*mpp,6),"max_m":round((int(xx[-1])-AXIS)*mpp,6)})
    return out


def run():
    shared=ROOT/"refs"
    shared.mkdir(parents=True,exist_ok=True)
    all_data={}
    montage=Image.new("RGB",(4*384,4*528),"#f3f0e8")
    draw=ImageDraw.Draw(montage)
    for ai,(key,d) in enumerate(ASSETS.items()):
        folder=ROOT/key
        ref=folder/"ref"
        ref.mkdir(parents=True,exist_ok=True)
        shutil.copy2(SOURCE/d["file"],ref/"approved-sheet.png")
        im=Image.open(SOURCE/d["file"]).convert("RGB")
        hpx=d["floor"]-d["top"]
        mpp=d["height"]/hpx
        data={"asset":key,"name":d["name"],"prefix":d["prefix"],"height_m":d["height"],"m_per_px":mpp,
              "canvas_px":CANVAS,"axis_col":AXIS,"ground_row":GROUND,"subject_height_px":hpx,
              "source_ground_row":d["floor"],"subject_frac":hpx/CANVAS[1],"ground_frac":(CANVAS[1]-GROUND)/CANVAS[1],
              "convention":"metres, Blender Z up, front -Y; front image +x = world +X; side faces image right, side +x = world -Y (az270)",
              "views":{},"front_landmarks":{},"side_landmarks":{},"ratios":{}}
        for vi,(view,box,cx) in enumerate(zip(["front","side","back","threequarter"],d["boxes"],d["axes"])):
            crop=im.crop(box)
            mask=clean_mask(crop,key,vi,box,d["floor"])
            # Keep the known sole row as the bottom authority; shadow below it is not anatomy.
            mask[max(0,d["floor"]-box[1]+1):]=False
            rgba=crop.convert("RGBA")
            rgba.putalpha(Image.fromarray(np.uint8(mask)*255))
            canvas=Image.new("RGBA",CANVAS,(255,255,255,0))
            px=AXIS-(cx-box[0]); py=GROUND-(d["floor"]-box[1])
            canvas.paste(rgba,(px,py))
            canvas.save(ref/f"{view}_clean.png")
            opaque=Image.new("RGB",CANVAS,"white")
            opaque.paste(canvas,mask=canvas.getchannel("A"))
            opaque.save(ref/f"{view}.png")
            m=np.asarray(canvas.getchannel("A"))>127
            Image.fromarray(np.uint8(m)*255).save(ref/f"{view}_mask.png")
            ys,xs=np.nonzero(m)
            bbox=[int(xs.min()),int(ys.min()),int(xs.max())+1,int(ys.max())+1]
            data["views"][view]={"source_box_px":box,"source_axis_x":cx,"camera_az":{ "front":0,"side":270,"back":180,"threequarter":-30}[view],
                "camera_el":0 if view!="threequarter" else 3,"lens":0 if view!="threequarter" else 70,
                "authority":"dimension guide; generated approximate orthographic" if view!="threequarter" else "appearance only; never metric",
                "bbox_clean_px":bbox,"width_m":(bbox[2]-bbox[0])*mpp,"height_m":(bbox[3]-bbox[1])*mpp,
                "alpha_area":int(m.sum()),"profile":silhouette_profile(m,mpp)}
            thumb=opaque.resize((384,528),Image.Resampling.LANCZOS)
            montage.paste(thumb,(vi*384,ai*528))
            draw.text((vi*384+12,ai*528+8),f'{d["name"]} / {view}',fill="#202226")
        for field,view_i in [("front_landmarks",0),("side_landmarks",1)]:
            lm=d["landmarks"] if view_i==0 else d["side_landmarks"]
            for name,(x,y) in lm.items():
                offset=(x-d["axes"][view_i])*mpp
                data[field][name]={"source_px":[x,y],"crop_px":[x-d["axes"][view_i]+AXIS,y-d["floor"]+GROUND],
                    "xz_m" if view_i==0 else "yz_m":[round(offset if view_i==0 else -offset,6),round((d["floor"]-y)*mpp,6)],
                    "status":"joint centre estimated under garment" if any(s in name for s in ["shoulder","elbow","waist","knee"]) else "visually measured landmark (~2px precision)"}
        front=d["landmarks"]
        ratios={
            "face_height_to_total":(d["chin"]-d["head_top"])/hpx,
            "face_width_to_total":(d["head_span"][1]-d["head_span"][0])/hpx,
            "head_with_hair_or_hat_height_to_total":(d["chin"]-d["top"])/hpx,
            "head_with_hair_or_hat_width_to_total":(d["hair_span"][1]-d["hair_span"][0])/hpx,
            "ear_span_to_total":(d["ear_span"][1]-d["ear_span"][0])/hpx,
            "shoulder_outer_width_to_total":(d["shoulder_span"][1]-d["shoulder_span"][0])/hpx,
            "garment_hip_width_to_total":(d["hip_span"][1]-d["hip_span"][0])/hpx,
            "shoulder_height_to_total":(d["floor"]-front["shoulder_left"][1])/hpx,
            "elbow_height_to_total":(d["floor"]-front["elbow_left"][1])/hpx,
            "wrist_height_to_total":(d["floor"]-front["wrist_left"][1])/hpx,
            "fingertip_height_to_total":(d["floor"]-front["fingertip_left"][1])/hpx,
            "wrist_span_to_total":(front["wrist_right"][0]-front["wrist_left"][0])/hpx,
            "ankle_spacing_to_total":(front["ankle_right"][0]-front["ankle_left"][0])/hpx,
            "pupil_spacing_to_total":(front["pupil_right"][0]-front["pupil_left"][0])/hpx,
            "foot_center_spacing_to_total":(front["foot_right"][0]-front["foot_left"][0])/hpx,
        }
        data["ratios"]={k:round(v,6) for k,v in ratios.items()}
        data["direct_spans_px"]={k:d[k] for k in ["head_span","ear_span","hair_span","headgear_span","shoulder_span","hip_span"]}
        data["direct_vertical_px"]={k:d[k] for k in ["top","head_top","chin","floor"]}
        data["inferred"]=d["inference"]
        data["mask_method"]="Deterministic colour/contour threshold, connected components, one-pixel closing, manual source-space pale-fabric segmentation polygons, ground-line suppression and hole filling; no resizing among this asset's panels. About 1-2 px boundary uncertainty. Original source preserved."
        (ref/"measurements.json").write_text(json.dumps(data,indent=2),encoding="utf8")
        all_data[key]={k:v for k,v in data.items() if k!="views"}
        all_data[key]["views"]={v:{k:x for k,x in e.items() if k!="profile"} for v,e in data["views"].items()}
        ratios_text="; ".join(f"{k}={v:.5f}" for k,v in ratios.items())
        views_text="\n".join(f"- {v}: source pixel box {tuple(e['source_box_px'])}; azimuth {e['camera_az']}°, elevation {e['camera_el']}°, lens {e['lens']} (0=orthographic); {e['authority']}. Canvas subject fraction {hpx/704:.6f}, ground row 688/704." for v,e in data["views"].items())
        brief=f'''ASSET      {d["prefix"]} — {d["name"]} ({key})
CATEGORY   character | biped, approved family protagonist
VIEWS
{views_text}
Source approved-sheet.png is a generated concept turnaround. Treat nominal orthographic views as dimensional guides, not physically consistent photographs. Front is dimension authority; profile supplies depth; back supplies costume coverage. Expressions and three-quarter supply surface character only. No panel independently rescaled.
SCALE      Height {d["height"]:.2f} m, using the target height printed on the approved sheet. Front crown row {d["top"]}, sole/ground row {d["floor"]}: {hpx} pixels; {mpp:.9f} m/pixel. Front outer width {data["views"]["front"]["width_m"]:.4f} m; side outer depth {data["views"]["side"]["width_m"]:.4f} m, including extremities. Origin on ground at front source x={d["axes"][0]}, profile x={d["axes"][1]}. Canonical metres, Blender Z up, facing -Y. Reference canvas 512x704, axis column 256, ground row 688. Side faces image-right and must use azimuth 270, not 90.
PROPORTIONS {ratios_text}
Detailed front/profile pixel landmarks and world coordinates: ref/measurements.json. Joint centres obscured by cloth are flagged estimates. Width profiles retain arm/body gaps and both legs; use these in silhouette comparisons. Neutral source pose is a low A-pose, arms about 15–23° from vertical; do not compare a T-pose against it.
PARTS      {d["parts"]}
SILHOUETTE {d["silhouette"]}
MATERIALS  {d["costume"]}. Palette by role: {", ".join(d["colors"])}. Skin smooth matte, roughness 0.48–0.64; woven clothes 0.65–0.82; hair and moustache 0.5–0.68; sandals rubber/leather 0.6–0.8; eyes 0.15–0.26. No metallic skin, black hard contour shell, or baked studio lighting. Cloth patterns should follow body volume and remain readable at 128 px.
ARTICULATION Humanoid deformation family compatible with the existing game's player/NPC controller and its idle, walk, run, ride, wave, sit, panic actions. Neck, shoulders, elbows, hips, knees, ankles, wrists, fingers; garment bones where continuous skirt needs independent motion. Hand/head/chest/foot sockets as needed. One stable skeleton and no negative scales. Preserve meaningful character-specific proportions.
INFERRED   {d["inference"]} Hidden anatomy, sole tread, exact finger backside geometry, dress/sarong inner surfaces and garment thickness are inferred. Orthographic height/width drift, subtle perspective, printed-pattern drift and facial asymmetry occur in these generated sheets. Face landmarks are visual estimates at ±2 px, so millimetre-accurate identity is not supported. Side depth is approximate; front dimensions win conflicts. Source floral patterns are authored decoration, not an exact atlas. Use clean silhouettes as evidence, never alter them to improve a score.
TARGET     Unity project KampungRun; existing FBX humanoid pipeline, plus GLB review/export when useful. Planning tier standard character: 20–35k LOD0, 8–15k LOD1, 2–5k LOD2; 1–2k shared atlases and 1–3 material groups where feasible. Existing chase camera and street scale retained by game integration. Review orthographic front/profile/back, three-quarter at 50 mm, gameplay character height 128 px. Budgets are planning targets; record actual exported and engine counts.

Preparation provenance: reference_prep.py, NumPy/Pillow/SciPy analytic segmentation. approved-sheet.png is unchanged. *_clean.png carry alpha masks and are suited for world_gate.py; *.png without _clean have white background and are suited for init_master.py / visual comparison. *_mask.png are white foreground on black. Each row profile is stored in measurements.json.

World gate command parameters: --matte ref/front_clean.png --az 0 --m-per-px {mpp:.9f} --axis-col 256 --ground-row 688. For profile use side_clean.png --az 270. Ground and scale errors must not be normalised away.
'''
        (folder/"asset-brief.md").write_text(brief,encoding="utf8")
    (shared/"family_measurements.json").write_text(json.dumps(all_data,indent=2),encoding="utf8")
    montage.save(shared/"prepared_references_contact_sheet.png")
    print(json.dumps({key:{"height_m":d["height_m"],"m_per_px":d["m_per_px"],"ratios":d["ratios"]} for key,d in all_data.items()},indent=2))


if __name__=="__main__":
    run()
