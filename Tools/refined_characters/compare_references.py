"""Strict world-registered comparisons for approved family reference mattes.

python compare_references.py --asset chr_pakmat --blend path/to/master.blend
python compare_references.py --asset chr_pakmat --render-dir existing/gate --views front
python compare_references.py --self-test

No silhouette crop, independent scale fitting, recentering or pose guessing occurs.
The optional Blender stage renders the reference's exact world window. Low-A pose
is a named, reported pose operation; it never changes the saved source .blend.
"""
from pathlib import Path
import argparse
from datetime import datetime,timezone
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys

HERE=Path(__file__).resolve().parent
VIEW_AZ={"front":0,"side":270,"back":180}
MEASURED_ARM_LOWER={"chr_pakmat":68,"chr_maksom":68,"chr_along":71,"chr_adik":61}


def parser():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--asset",choices=["chr_pakmat","chr_maksom","chr_along","chr_adik"])
    p.add_argument("--blend",type=Path)
    p.add_argument("--render-dir",type=Path,help="previous registered output from this script")
    p.add_argument("--out",type=Path)
    p.add_argument("--blender",type=Path)
    p.add_argument("--views",default="front,side,back")
    p.add_argument("--collections",default="LOW")
    p.add_argument("--pose",choices=["low-a","rest","saved"],default="low-a")
    p.add_argument("--arm-lower",type=float,help="T-pose-to-low-A angle in degrees; defaults to measured per-character source pose")
    p.add_argument("--supersample",type=int,default=2)
    p.add_argument("--threads",type=int,default=4,help="CPU threads for the Blender subprocess")
    p.add_argument("--stage",choices=["blockout","forms"],default="blockout")
    p.add_argument("--self-test",action="store_true")
    p.add_argument("--summary",action="store_true",help="aggregate existing family latest_world_comparison evidence")
    p.add_argument("--summary-label",choices=["latest","final"],default="latest")
    p.add_argument("--render-stage",action="store_true",help=argparse.SUPPRESS)
    return p


def render_registered(args):
    import bpy
    import numpy as np
    from mathutils import Matrix,Vector
    data=json.loads((HERE/args.asset/"ref"/"measurements.json").read_text())
    bpy.ops.wm.open_mainfile(filepath=str(args.blend.resolve()))
    if args.pose!="saved":
        for arm in [o for o in bpy.data.objects if o.type=="ARMATURE"]:
            if arm.animation_data:
                arm.animation_data.action=None
                for t in arm.animation_data.nla_tracks:
                    t.mute=True
            for pb in arm.pose.bones:
                pb.rotation_mode="QUATERNION"
                pb.rotation_quaternion=(1,0,0,0)
                pb.location=(0,0,0)
                pb.scale=(1,1,1)
            if args.pose=="low-a":
                for side,sign in [("Left",1),("Right",-1)]:
                    pb=arm.pose.bones.get(side+"UpperArm")
                    if pb is None:
                        raise RuntimeError(f"Low-A pose requires {side}UpperArm; select --pose rest or saved explicitly for another rig")
                    rest=pb.bone.matrix_local.to_3x3()
                    rot=Matrix.Rotation(math.radians(sign*args.arm_lower),3,"Y")
                    pb.rotation_quaternion=(rest.inverted()@rot@rest).to_quaternion()
    keep=set()
    for name in args.collections.split(","):
        col=bpy.data.collections.get(name)
        if col:
            keep.update(o for o in col.all_objects if o.type=="MESH" and "LOD1" not in o.name and "LOD2" not in o.name)
    if not keep:
        keep={o for o in bpy.data.objects if o.type=="MESH" and o.name.startswith(args.asset) and "LOD0" in o.name}
    if not keep:
        raise RuntimeError("No deliverable meshes selected; refusing an empty or arbitrary-scene comparison")
    for o in bpy.data.objects:
        if o.type in {"MESH","CURVE","CURVES","META","FONT","SURFACE"}:
            o.hide_render=o not in keep
        if o in keep:
            o.hide_set(False)
            o.hide_viewport=False
    bpy.context.view_layer.update()
    scene=bpy.context.scene
    scene.render.engine="BLENDER_WORKBENCH"
    sh=scene.display.shading
    sh.light="STUDIO"
    sh.color_type="SINGLE"
    sh.single_color=(.57,.59,.62)
    sh.show_shadows=False
    sh.show_cavity=False
    sh.show_object_outline=False
    scene.render.film_transparent=True
    scene.render.image_settings.file_format="PNG"
    scene.render.image_settings.color_mode="RGBA"
    scene.render.resolution_percentage=100
    scene.view_settings.view_transform="Standard"
    scene.view_settings.exposure=0
    scene.view_settings.gamma=1
    width,height=data["canvas_px"]
    scale=data["m_per_px"]
    scene.render.resolution_x=width*args.supersample
    scene.render.resolution_y=height*args.supersample
    camera=bpy.data.objects.new("REF_COMPARE_Camera",bpy.data.cameras.new("REF_COMPARE_Camera"))
    scene.collection.objects.link(camera)
    camera.data.type="ORTHO"
    camera.data.sensor_fit="HORIZONTAL"
    camera.data.ortho_scale=width*scale
    camera.data.clip_start=.01
    camera.data.clip_end=100
    scene.camera=camera
    x_center=(width/2-data["axis_col"])*scale
    z_center=(data["ground_row"]-height/2)*scale
    args.out.mkdir(parents=True,exist_ok=True)
    registration={"asset":args.asset,"blend":str(args.blend.resolve()),
        "blend_sha256":hashlib.sha256(args.blend.read_bytes()).hexdigest(),"rendered_utc":datetime.now(timezone.utc).isoformat(),
        "reference_measurements":str(HERE/args.asset/"ref"/"measurements.json"),
        "m_per_px":scale,"axis_col":data["axis_col"],"ground_row":data["ground_row"],"canvas_px":[width,height],
        "supersample":args.supersample,"pose":args.pose,"arm_lower_degrees":args.arm_lower if args.pose=="low-a" else None,
        "objects":[o.name for o in sorted(keep,key=lambda o:o.name)],"views":{}}
    for view in args.views.split(","):
        az=math.radians(VIEW_AZ[view])
        right=Vector((math.cos(az),math.sin(az),0))
        toward=Vector((math.sin(az),-math.cos(az),0))
        camera.location=right*x_center+Vector((0,0,z_center))+toward*20
        camera.rotation_mode="QUATERNION"
        camera.rotation_quaternion=(-toward).to_track_quat("-Z","Y")
        scene.render.filepath=str(args.out/f"registered_{view}.png")
        bpy.ops.render.render(write_still=True)
        registration["views"][view]={"azimuth":VIEW_AZ[view],"camera_location":list(camera.location),"ortho_horizontal_width_m":width*scale}
    (args.out/"render-registration.json").write_text(json.dumps(registration,indent=2),encoding="utf8")
    print("REGISTERED_RENDER_COMPLETE",str(args.out))


def mask_bbox(mask):
    import numpy as np
    y,x=np.nonzero(mask)
    return [int(x.min()),int(y.min()),int(x.max())+1,int(y.max())+1] if len(x) else [0,0,0,0]


def metrics(ref,model,data,stage):
    import numpy as np
    if ref.shape!=model.shape:
        raise ValueError("Mask canvases differ; independent image fitting is forbidden")
    inter=int((ref&model).sum()); union=int((ref|model).sum())
    rb,mb=mask_bbox(ref),mask_bbox(model)
    sh=data["subject_height_px"]
    scale=data["m_per_px"]
    width_rows=[]
    for i in range(10):
        row=int(rb[1]+(i+.5)*(rb[3]-rb[1]-1)/10)
        rx=np.nonzero(ref[row])[0]; mx=np.nonzero(model[row])[0]
        rw=int(rx[-1]-rx[0]+1) if len(rx) else 0
        mw=int(mx[-1]-mx[0]+1) if len(mx) else 0
        width_rows.append({"band":i+1,"row":row,"z_m":round((data["ground_row"]-row)*scale,6),
            "reference_width_px":rw,"model_width_px":mw,"difference_m":round((mw-rw)*scale,6),
            "difference_fraction_reference_height":round((mw-rw)/sh,6),
            "occupied_reference_px":len(rx),"occupied_model_px":len(mx)})
    def centroid(m):
        y,x=np.nonzero(m)
        return [float(x.mean()),float(y.mean())] if len(x) else [0,0]
    rc,mc=centroid(ref),centroid(model)
    min_iou,max_band=(.85,.05) if stage=="blockout" else (.90,.03)
    result={"metric":"world-registered; no crop, translation or independent rescale", "stage":stage,
        "iou":round(inter/max(1,union),6),"reference_pixels":int(ref.sum()),"model_pixels":int(model.sum()),
        "reference_only_pct":round(100*int((ref&~model).sum())/max(1,int(ref.sum())),4),
        "model_only_pct_of_reference":round(100*int((model&~ref).sum())/max(1,int(ref.sum())),4),
        "reference_bbox_px":rb,"model_bbox_px":mb,
        "height_error_m":round(((mb[3]-mb[1])-(rb[3]-rb[1]))*scale,6),
        "height_error_pct":round(100*((mb[3]-mb[1])-(rb[3]-rb[1]))/max(1,rb[3]-rb[1]),4),
        "top_error_m":round((rb[1]-mb[1])*scale,6),
        "sole_error_m":round((rb[3]-mb[3])*scale,6),
        "width_error_m":round(((mb[2]-mb[0])-(rb[2]-rb[0]))*scale,6),
        "centroid_offset_m":[round((mc[0]-rc[0])*scale,6),round((rc[1]-mc[1])*scale,6)],
        "width_bands":width_rows,
        "criteria":{"minimum_iou":min_iou,"maximum_absolute_band_width_fraction":max_band},
        "scope":"Silhouette criteria only; this does not prove facial landmark accuracy, material fidelity, topology or animation quality."}
    result["silhouette_criteria_pass"]=result["iou"]>=min_iou and all(abs(b["difference_fraction_reference_height"])<=max_band for b in width_rows)
    return result


def compare(args):
    import numpy as np
    from PIL import Image,ImageDraw,ImageFont
    data=json.loads((HERE/args.asset/"ref"/"measurements.json").read_text())
    input_dir=args.render_dir or args.out
    registration_path=input_dir/"render-registration.json"
    if not registration_path.is_file() and (input_dir/"camera-contract.json").is_file():
        # Existing review renderer provides its full camera calibration. Resampling
        # via that world transform is allowed; aligning silhouette bounds is not.
        registration=import_registered_review(args,data,input_dir)
        input_dir=args.out
    else:
        registration=json.loads(registration_path.read_text())
    for key in ["m_per_px","axis_col","ground_row","canvas_px"]:
        if registration[key]!=data[key]:
            raise ValueError(f"Registration mismatch {key}: render={registration[key]} reference={data[key]}")
    if registration["asset"]!=args.asset:
        raise ValueError("Registration belongs to another character")
    args.out.mkdir(parents=True,exist_ok=True)
    font=ImageFont.load_default(size=17)
    reports={}
    for view in args.views.split(","):
        if registration["views"][view]["azimuth"]!=VIEW_AZ[view]:
            raise ValueError(f"Camera azimuth mismatch for {view}")
        reference=Image.open(HERE/args.asset/"ref"/f"{view}_clean.png").convert("RGBA")
        model=Image.open(input_dir/f"registered_{view}.png").convert("RGBA")
        s=registration["supersample"]
        if model.size!=(reference.width*s,reference.height*s):
            raise ValueError("Render dimensions differ from declared full-frame supersampling")
        # Whole-frame area downsampling retains camera and world coordinates.
        model=model.resize(reference.size,Image.Resampling.BOX)
        rm=np.asarray(reference.getchannel("A"))>127
        mm=np.asarray(model.getchannel("A"))>127
        report=metrics(rm,mm,data,args.stage)
        report.update({"asset":args.asset,"view":view,"pose":registration["pose"],"arm_lower_degrees":registration["arm_lower_degrees"]})
        if "source_camera_contract" in registration:
            report["source_camera_contract"]=registration["source_camera_contract"]
            report["registered_pixel_transform"]=registration["views"][view]
        reports[view]=report
        (args.out/f"comparison_{view}.json").write_text(json.dumps(report,indent=2),encoding="utf8")
        diff=np.full((reference.height,reference.width,3),239,dtype=np.uint8)
        diff[rm&mm]=(115,124,133)
        diff[rm&~mm]=(224,68,61)
        diff[mm&~rm]=(45,115,224)
        difference=Image.fromarray(diff)
        panels=[]
        for im in [reference,model]:
            back=Image.new("RGBA",im.size,(247,246,242,255))
            panels.append(Image.alpha_composite(back,im).convert("RGB"))
        panels.append(difference)
        w,h=reference.size
        sheet=Image.new("RGB",(w*3,h+258),(248,247,243))
        dr=ImageDraw.Draw(sheet)
        dr.text((18,12),f'{data["name"]} / {view} / IoU {report["iou"]:.3f} / {args.stage} silhouette {"PASS" if report["silhouette_criteria_pass"] else "FAIL"}',fill=(30,35,41),font=font)
        dr.text((18,37),f'Height error {report["height_error_m"]:+.3f}m | Missing {report["reference_only_pct"]:.1f}% | Extra {report["model_only_pct_of_reference"]:.1f}% | Pose {registration["pose"]}',fill=(65,70,76),font=font)
        for col,(title,im) in enumerate(zip(["APPROVED REFERENCE","BLENDER / SAME WORLD WINDOW","OVERLAY: RED missing / BLUE extra"],panels)):
            dr.text((col*w+18,64),title,fill=(30,35,41),font=font)
            sheet.paste(im,(col*w,90))
        # At 128 px reference character height; same scale used for all 3 panels.
        factor=128/data["subject_height_px"]
        for col,im in enumerate(panels):
            tiny=im.resize((round(w*factor),round(h*factor)),Image.Resampling.LANCZOS)
            sheet.paste(tiny,(col*w+18,h+88))
        dr.text((180,h+112),"Fixed scale gameplay strip; no independent fitting",fill=(65,70,76),font=font)
        sheet.save(args.out/f"comparison_{view}.png")
        print(f'{args.asset}/{view}: IoU={report["iou"]:.4f} missing={report["reference_only_pct"]:.2f}% extra={report["model_only_pct_of_reference"]:.2f}% height={report["height_error_m"]:+.4f}m silhouette_pass={report["silhouette_criteria_pass"]}')
    (args.out/"comparisons.json").write_text(json.dumps(reports,indent=2),encoding="utf8")


def import_registered_review(args,data,input_dir):
    from PIL import Image
    contract=json.loads((input_dir/"camera-contract.json").read_text())
    if contract["projection"]!="orthographic" or contract["elevation_degrees"]!=0:
        raise ValueError("Existing render must be orthographic with zero elevation")
    args.out.mkdir(parents=True,exist_ok=True)
    angle=re.search(r"lowered\s+([0-9.]+)\s+degrees",contract["pose"])
    registration={"asset":args.asset,"m_per_px":data["m_per_px"],"axis_col":data["axis_col"],
        "ground_row":data["ground_row"],"canvas_px":data["canvas_px"],"supersample":1,
        "pose":contract["pose"],"arm_lower_degrees":float(angle.group(1)) if angle else None,"source_camera_contract":str(input_dir/"camera-contract.json"),"views":{}}
    for view in args.views.split(","):
        cam=contract["cameras"][view]
        im=Image.open(input_dir/f"{view}-alpha.png").convert("RGBA")
        if list(im.size)!=contract["resolution_pixels"]:
            raise ValueError("Camera contract resolution differs from image")
        ppm=im.height/cam["vertical_ortho_scale_m"]
        k=data["m_per_px"]*ppm
        center=cam["target_center_m"]
        az=math.radians(cam["yaw_degrees"])
        horizontal_center=center[0]*math.cos(az)+center[1]*math.sin(az)
        difference=(cam["yaw_degrees"]-VIEW_AZ[view])%360
        if difference not in [0,180]:
            raise ValueError("Source and target cameras are not corresponding orthographic views")
        flip=-1 if difference==180 else 1
        a=flip*k
        c=im.width/2-data["axis_col"]*a-horizontal_center*ppm
        f=im.height/2-data["ground_row"]*k+center[2]*ppm
        affine=(a,0,c,0,k,f)
        aligned=im.transform(tuple(data["canvas_px"]),Image.Transform.AFFINE,affine,resample=Image.Resampling.BICUBIC)
        aligned.save(args.out/f"registered_{view}.png")
        registration["views"][view]={"azimuth":VIEW_AZ[view],"source_azimuth":cam["yaw_degrees"],
            "source_from_target_pixel_affine":affine,"opposite_flank_mirrored":flip<0,
            "note":"Opposite flank is mirrored using world camera axes; asymmetric features may differ" if flip<0 else "Full world-coordinate registration, no bbox fitting"}
    (args.out/"render-registration.json").write_text(json.dumps(registration,indent=2),encoding="utf8")
    return registration


def self_test():
    import numpy as np
    ref=np.zeros((100,80),bool); ref[10:90,25:55]=True
    data={"subject_height_px":80,"m_per_px":.02,"ground_row":90}
    same=metrics(ref,ref,data,"blockout")
    shifted=np.roll(ref,5,axis=1)
    short=np.zeros_like(ref); short[20:90,25:55]=True
    shift=metrics(ref,shifted,data,"blockout")
    shrink=metrics(ref,short,data,"blockout")
    assert same["iou"]==1 and same["height_error_m"]==0
    assert shift["iou"]<.8 and abs(shift["centroid_offset_m"][0]-.1)<1e-8
    assert shrink["iou"]<1 and shrink["height_error_m"]==-.2
    print("SELF_TEST_PASS: identical=1.0; translations and global scale changes lower IoU and retain metre deviations")


def summarize_latest(label="latest"):
    from PIL import Image,ImageDraw,ImageFont
    results={}
    rows=[]
    cells=[]
    for asset in MEASURED_ARM_LOWER:
        ref=json.loads((HERE/asset/"ref"/"measurements.json").read_text())
        out=HERE/asset/"review"/(label+"_world_comparison")
        if not (out/"comparisons.json").is_file():
            continue
        reports=json.loads((out/"comparisons.json").read_text())
        registration=json.loads((out/"render-registration.json").read_text())
        results[asset]={"name":ref["name"],"reference_height_m":ref["height_m"],
            "source_blend":registration.get("blend"),"source_blend_sha256":registration.get("blend_sha256"),
            "rendered_utc":registration.get("rendered_utc"),"views":reports}
        for view,report in reports.items():
            width=max(report["width_bands"],key=lambda b:abs(b["difference_m"]))
            rows.append(f'| {ref["name"]} | {view} | {report["iou"]:.4f} | {report["height_error_m"]*1000:+.1f} mm | {width["difference_m"]*100:+.1f} cm at z={width["z_m"]:.3f} m |')
            cells.append(Image.open(out/f"comparison_{view}.png").convert("RGB").resize((768,481),Image.Resampling.LANCZOS))
    folder=HERE/"refs"
    folder.mkdir(parents=True,exist_ok=True)
    payload={"generated_utc":datetime.now(timezone.utc).isoformat(),"scope":"Reference silhouette audit; independent of runtime, material and animation acceptance", "assets":results}
    (folder/f"family_{label}_comparisons.json").write_text(json.dumps(payload,indent=2),encoding="utf8")
    report=f"# {label.capitalize()} family reference comparison snapshot\n"+"""

Exact world registration: metres, Z up, front -Y, orthographic front azimuth 0 and profile azimuth 270. Reference source scale and ground stay fixed. Models are posed using measured character-specific neutral arm angles; images are never fitted by their silhouette bounds.

The table measures reference approximation; it is **not a claim of final asset acceptance or exact reconstruction**. Individual JSON files record the blockout silhouette criteria: IoU >= 0.85 and every sampled band width within 5% of reference height. The stricter forms gate, face accuracy, material fidelity, topology, deformation, animation and Unity integration each require separate evidence. Failed views remain failures.

| Character | View | IoU | Height error | Largest width/depth error |
|---|---|---:|---:|---|
"""+"\n".join(rows)+"""

## Reference limits and inferred information

- The approved sheets are generated concepts, not surveyed orthographic blueprints. Front proportions take priority; profile panels guide depth and rear panels guide coverage.
- Alpha boundaries have about 1–2 pixels of uncertainty, approximately 2–6 mm depending on the character. Pale fabric and ground shadows required explicit analytic cleanup; untouched source sheets are preserved beside the mattes.
- The profile horizontal origin is inferred from the pelvis/ankle axis. Total profile widths are independent of that origin choice. Do not excuse large depth or deformation errors as camera ambiguity.
- View-to-view hair, facial contour, cloth patterns and sandal straps vary. Adik's profile crown is about 4 source pixels below the front crown. No unsupported per-view score ceiling has been claimed.
- Hidden anatomy, garment interiors and physical cloth thickness are inferred. Each asset brief lists the details.

Full comparison images, exact camera registrations, per-band measurements and missing/extra silhouette coverage are saved with this snapshot. Rendered deformation defects require repair even where a silhouette score happens to pass.
"""+f'\nDetail folder: `<character>/review/{label}_world_comparison/`. Contact sheet: `family_{label}_comparisons.png`.\n'
    (folder/f"family_{label}_comparisons.md").write_text(report,encoding="utf8")
    if cells:
        montage=Image.new("RGB",(1536,481*math.ceil(len(cells)/2)),"white")
        for index,cell in enumerate(cells):
            montage.paste(cell,((index%2)*768,(index//2)*481))
        montage.save(folder/f"family_{label}_comparisons.png")
    print("FAMILY_COMPARISON_SUMMARY",str(folder/f"family_{label}_comparisons.md"))


def main():
    argv=sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else sys.argv[1:]
    args=parser().parse_args(argv)
    if args.self_test:
        self_test(); return
    if args.summary:
        summarize_latest(args.summary_label); return
    if not args.asset:
        raise SystemExit("--asset is required")
    if args.arm_lower is None:
        args.arm_lower=MEASURED_ARM_LOWER[args.asset]
    if any(v not in VIEW_AZ for v in args.views.split(",")):
        raise SystemExit("Only front,side,back are measurable; three-quarter is appearance-only")
    if args.supersample<1:
        raise SystemExit("--supersample must be a positive integer")
    args.out=args.out or HERE/args.asset/"review"/"world_comparison"
    if args.render_stage:
        render_registered(args); return
    if args.blend:
        blender=args.blender or os.environ.get("BLENDER_BIN") or shutil.which("blender")
        if not blender:
            candidate=Path(r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
            blender=candidate if candidate.is_file() else None
        if not blender:
            raise SystemExit("Blender not found; provide --blender")
        args.out.mkdir(parents=True,exist_ok=True)
        command=[str(blender),"--background","--threads",str(args.threads),"--python",str(Path(__file__).resolve()),"--",
            "--render-stage","--asset",args.asset,"--blend",str(args.blend.resolve()),"--out",str(args.out.resolve()),
            "--views",args.views,"--collections",args.collections,"--pose",args.pose,"--arm-lower",str(args.arm_lower),"--supersample",str(args.supersample)]
        with (args.out/"registered-render.log").open("w",encoding="utf8") as log:
            subprocess.run(command,stdout=log,stderr=subprocess.STDOUT,check=True)
    elif not args.render_dir:
        raise SystemExit("Provide --blend to render or --render-dir with a render-registration.json")
    compare(args)


if __name__=="__main__":
    main()
