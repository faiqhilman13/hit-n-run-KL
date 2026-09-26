"""Stage refined source + exports, then explicitly publish validated stable FBX IDs.

Run in Blender: blender -b -t 4 -P Tools/refined_characters/build_characters.py -- chr_pakmat
Use --publish only after inspecting the staged review. Rest pose and all 15 existing
actions are preserved. No scene/controller/world assets are rebuilt.
"""
import bpy,sys,os,math,json,shutil,argparse
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
PROJECT=HERE.parent.parent
sys.path.insert(0,str(PROJECT/'Tools/blender/kl'))
import kl_core as K
import kl_humanoid as H
import kl_refined as R

def character_atlas(path):
    source=PROJECT/'Assets/Models/KL/kl_palette.png'
    im=bpy.data.images.load(str(source),check_existing=False)
    im.name='kl_character_palette';im.alpha_mode='CHANNEL_PACKED'
    data=list(im.pixels[:]);w,h=im.size
    for name,rgb in R.COLORS.items():
        idx=K.PAL_INDEX[name];cx=idx%K.CELLS;cy=idx//K.CELLS
        for y in range(K.CELL_PX):
            for x in range(K.CELL_PX):
                offset=((h-1-(cy*K.CELL_PX+y))*w+cx*K.CELL_PX+x)*4
                data[offset:offset+3]=rgb
    # Soft broad eye highlights; alpha remains material data, never opacity.
    for name,gloss in [('eye_white',.27),('pupil',.38),('skin',.035)]:
        idx=K.PAL_INDEX[name];cx=idx%K.CELLS;cy=idx//K.CELLS
        for y in range(K.CELL_PX):
            for x in range(K.CELL_PX):
                offset=((h-1-(cy*K.CELL_PX+y))*w+cx*K.CELL_PX+x)*4
                data[offset+3]=(128+round(gloss*127))/255
    im.pixels=data;im.filepath_raw=str(path);im.file_format='PNG';im.save()
    return im

def material(im):
    mat=bpy.data.materials.new('M_KL_Character');mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.72
    bs.inputs['Specular IOR Level'].default_value=.23
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;tex.interpolation='Closest'
    mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    return mat

def lod_copy(ob,arm,col):
    lod=ob.copy();lod.data=ob.data.copy();lod.name=ob.name.replace('_LOD0','_LOD1');col.objects.link(lod)
    lod.shape_key_clear();lod.modifiers.clear()
    dec=lod.modifiers.new('DistanceSimplification','DECIMATE');dec.ratio=.28;dec.delimit={'UV'}
    bpy.context.view_layer.objects.active=lod
    bpy.ops.object.modifier_apply(modifier=dec.name)
    # Collapse simplification can merge joint influences. Enforce the same four
    # normalised weights that the runtime and full-detail mesh use.
    for v in lod.data.vertices:
        keep=sorted(((g.group,g.weight) for g in v.groups if g.weight>1e-7),key=lambda x:-x[1])[:4]
        total=sum(w for _,w in keep)
        for g in list(v.groups):lod.vertex_groups[g.group].remove([v.index])
        for gi,w in keep:lod.vertex_groups[gi].add([v.index],w/total,'REPLACE')
    mod=lod.modifiers.new('Armature','ARMATURE');mod.object=arm
    lod.hide_render=True;lod.hide_set(True)
    return lod

def calibrate(scene,cols,p,cid):
    # Reference collection retains metre scale, ground and a ruler in every master.
    for name in ['REF','HIGH','LOW','RIG_CTRL','RIG_DEF','COLLISION','SOCKETS']:
        if name not in cols:
            c=bpy.data.collections.new(name);scene.collection.children.link(c);cols[name]=c
    scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    ruler=bpy.data.objects.new('REF_height_'+cid,None);cols['REF'].objects.link(ruler)
    ruler.empty_display_type='SINGLE_ARROW';ruler.empty_display_size=p['height'];ruler.hide_render=True
    ruler.location=(-.8,0,0)
    for side in ('Left','Right'):
        socket=bpy.data.objects.new('SOCKET_hand.'+('L' if side=='Left' else 'R'),None)
        cols['SOCKETS'].objects.link(socket);socket.empty_display_type='ARROWS';socket.empty_display_size=.06
        socket.hide_render=True
    refdir=HERE/cid/'ref'
    for view,loc,rot in [('front',(0,.6,0),(math.pi/2,0,0)),('side',(.65,0,0),(math.pi/2,0,math.pi/2))]:
        src=refdir/(view+'.png')
        if src.exists():
            ob=bpy.data.objects.new('REF_'+view,None);cols['REF'].objects.link(ob)
            ob.empty_display_type='IMAGE';ob.data=bpy.data.images.load(str(src));ob.empty_display_size=p['height']*1.17
            ob.location=loc;ob.location.z=p['height']/2;ob.rotation_euler=rot;ob.hide_render=True;ob.hide_set(True)

def build(cid,publish=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    folder=HERE/cid;folder.mkdir(exist_ok=True);(folder/'exports').mkdir(exist_ok=True)
    (folder/'textures').mkdir(exist_ok=True);(folder/'build').mkdir(exist_ok=True)
    scene=bpy.context.scene;scene.render.fps=30
    cols={}
    for name in ['LOW','RIG_DEF','REF','HIGH','RIG_CTRL','COLLISION','SOCKETS']:
        cols[name]=bpy.data.collections.new(name);scene.collection.children.link(cols[name])
    im=character_atlas(folder/'textures/kl_character_palette.png');mat=material(im)
    m,p=R.build(cid)
    if m.tags.get('concealed_anatomy'):
        # Preserve uncropped reference source in HIGH, never exported.
        full=bpy.data.meshes.new(cid+'_CoveredAnatomySource');m.bm.to_mesh(full)
        high=bpy.data.objects.new(cid+'_CoveredAnatomySource',full);cols['HIGH'].objects.link(high)
        high.hide_render=True;high.hide_set(True);full.materials.append(mat)
        import bmesh
        bmesh.ops.delete(m.bm,geom=m.tags['concealed_anatomy'],context='VERTS')
        m.weights=[(v,w) for v,w in m.weights if v.is_valid]
        for tag,vs in m.tags.items():m.tags[tag]=[v for v in vs if v.is_valid]
    ob,weights,tags=m.to_object(cid+'_LOD0',mat,cols['LOW'])
    arm=H.build_armature(cid,p,cols['RIG_DEF']);arm.name=cid
    # Refined builders already author deliberate normalised smooth weights. Do not
    # let the old primitive post-process override skirt or shoulder influences.
    H.skin(ob,arm,weights);H.shape_keys(ob,tags,R.FACE_KEYS)
    clips=H.author_clips(arm,cid)
    lod=lod_copy(ob,arm,cols['LOW']);calibrate(scene,cols,p,cid)
    for socket in cols['SOCKETS'].objects:
        if socket.name.startswith('SOCKET_hand.'):
            side='Left' if socket.name.endswith('.L') else 'Right'
            socket.parent=arm;socket.parent_type='BONE';socket.parent_bone=side+'Hand'
    ob['owner']='phase:06_rig';ob['reference_status']='approved concept; inferred depth recorded in brief'
    arm['proportions_json']=json.dumps(p)
    for img in bpy.data.images:
        if img.source=='FILE':img.pack()
    blend=folder/(cid+'_master.blend');bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    lod.hide_set(False);lod.hide_render=False
    fbx=folder/'exports'/(cid+'.fbx')
    K.export_fbx([arm,ob,lod],str(fbx),armature=True,anims=True,path_mode='STRIP')
    shutil.copy2(folder/'textures/kl_character_palette.png',folder/'exports/kl_character_palette.png')
    lod.hide_set(True);lod.hide_render=True
    stats=dict(asset=cid,height_m=p['height'],mesh_height_m=round(ob.dimensions.z,5),
        triangles=sum(len(f.vertices)-2 for f in ob.data.polygons),lod1_triangles=sum(len(f.vertices)-2 for f in lod.data.polygons),
        bones=len(arm.data.bones),clips=clips,shape_keys=[k.name for k in ob.data.shape_keys.key_blocks],
        bounds_m=list(ob.dimensions),palette='kl_character_palette.png',source=str(blend),export=str(fbx),
        rig_contract='Existing Unity Humanoid, T pose, 23 bones, metre scale, +Z Unity forward',
        inferred=['occluded anatomy','garment interior','finger depth','NPC adaptations from approved family style'],
        material_contract='sRGB base color palette, alpha gloss metadata, no directional light baked; smooth vertex normals',
        reference_status='Candidate reconstruction; consult measured review deviations before acceptance')
    (folder/'exports/asset-manifest.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
    (folder/'exports/animation-contract.json').write_text(json.dumps({name:{'frames':length,'loop':loop,'fps':30} for name,(length,loop,_) in H.clip_defs().items()},indent=2))
    # Numbered reproducible entry point and copied template provenance.
    (folder/'build/06_rig.py').write_text('# Reproducible build of this asset; shared helpers own only this asset.\nimport runpy,sys\nsys.argv=["build_characters.py","--","'+cid+'"]\nrunpy.run_path('+repr(str(HERE/'build_characters.py'))+',run_name="__main__")\n')
    if publish:
        shutil.copy2(fbx,PROJECT/'Assets/Models/KL'/(cid+'.fbx'))
        shutil.copy2(folder/'textures/kl_character_palette.png',PROJECT/'Assets/Models/KL/kl_character_palette.png')
    print('REFINED_BUILD',json.dumps(stats),flush=True)
    return stats

if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    ap=argparse.ArgumentParser();ap.add_argument('assets',nargs='*');ap.add_argument('--publish',action='store_true')
    ns=ap.parse_args(args)
    for cid in ns.assets or list(R.SPECS):build(cid,ns.publish)
