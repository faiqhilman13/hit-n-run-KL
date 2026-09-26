"""Fuse tagged trouser pelvis/leg surfaces without touching shoes or garments.

The caller tags only its pelvis and leg-loft vertices as ``pants`` and calls
``fuse_pants(m, p, s)`` after limb construction. Palette cells come from the
original source vertices; deformation weights are reconstructed analytically.
Metres, +Z up, -Y forward. No reference-match claim is made by this operation.
"""
import math
import bmesh
import bpy
from mathutils.kdtree import KDTree

import kl_core as K
import kl_humanoid as H


def _smooth(t):
    t=max(0.,min(1.,float(t)))
    return t*t*(3.-2.*t)


def _weights(co,p,s):
    """Stable central pelvis, smooth side assignment and knee/ankle chains."""
    hz,kz,az=p['hip_z'],p['knee_z'],p['ankle_z']
    hx=max(.03,p['hip_x'])
    child=bool(s.get('child'))
    span=.13 if child else .18
    descend=_smooth((hz+.025-co.z)/span)
    # Central crotch vertices stay on Hips near the waist, gradually handing off
    # below it. This avoids a hard Left/Right weight seam through the pelvis.
    center=1.-_smooth(abs(co.x)/(hx*.60))
    center_hold=.72*center*_smooth((co.z-(hz-.13))/.09)
    hips=1.-descend*(1.-center_hold)
    left=_smooth(.5+co.x/max(.035,hx*.52))
    result={'Hips':hips}
    for side,fraction in [('Left',left),('Right',1.-left)]:
        leg=H._chain(-co.z,[(-kz,side+'UpperLeg',side+'LowerLeg',.035 if child else .05),
                           (-az,side+'LowerLeg',side+'Foot',.023 if child else .03)])
        for name,value in leg.items():
            result[name]=result.get(name,0.)+(1.-hips)*fraction*value
    result=dict(sorted(((k,v) for k,v in result.items() if v>1e-8),key=lambda item:-item[1])[:4])
    total=sum(result.values())
    if total<=0 or not math.isfinite(total):
        raise ValueError('Invalid pants weight normalization')
    return {key:value/total for key,value in result.items()}


def _components(mesh):
    roots=list(range(len(mesh.vertices)))
    def find(i):
        while roots[i]!=i:
            roots[i]=roots[roots[i]]
            i=roots[i]
        return i
    for edge in mesh.edges:
        a,b=edge.vertices
        roots[find(a)]=find(b)
    return len({find(i) for i in range(len(roots))})


def fuse_pants(m,p,s):
    """Replace tagged pelvis/leg geometry with a connected, budgeted surface.

    Returns a small observable geometry report. Missing tags and skirt/dress
    characters are no-ops. Invalid/shared selections fail before deleting source.
    """
    if s.get('skirt') or s.get('dress'):
        return None
    if getattr(m,'pants_fusion_report',None):
        return m.pants_fusion_report
    chosen={v for v in m.tags.get('pants',[]) if v.is_valid}
    if not chosen:
        return None
    faces=[]
    for f in m.bm.faces:
        selected=[v in chosen for v in f.verts]
        if all(selected):
            faces.append(f)
        elif any(selected):
            raise ValueError('Pants tag crosses an unselected face; tag complete pelvis and leg-loft components only')
    if not faces:
        return None
    m.bm.verts.index_update()
    original=sorted(chosen,key=lambda v:v.index)
    index={v:i for i,v in enumerate(original)}
    original_tris=sum(len(f.verts)-2 for f in faces)
    # Long trousers need uniform cells at the deforming knees/crotch. Preserve
    # the already reviewed short-trouser construction, which stops above them.
    uniform=not s.get('shorts')
    budget=12000 if uniform else max(128,min(original_tris,10000))
    # Broad adult trousers exceed 12k at .016 m; a .017 m uniform grid preserves
    # the same topology policy while keeping those three variants under budget.
    spacing=(.017 if s.get('bodyw',.2)>.26 else .016) if uniform else .006
    tree=KDTree(len(original))
    for i,v in enumerate(original):
        tree.insert(v.co,i)
    tree.balance()
    colors={}
    votes={}
    for f in faces:
        uv=tuple(f.loops[0][m.uv].uv)
        for v in f.verts:
            vc=votes.setdefault(v,{})
            vc[uv]=vc.get(uv,0)+1
    for v in original:
        colors[v]=max(votes.get(v,{K.pal_uv(s['pants']):1}).items(),key=lambda item:item[1])[0]

    mesh=bpy.data.meshes.new('REFINED_TrouserUnion_Source')
    mesh.from_pydata([tuple(v.co) for v in original],[],[[index[v] for v in f.verts] for f in faces])
    mesh.update()
    # Loft caps and spheres can have opposite winding conventions. Recalculate
    # each closed component before the signed-volume remesher samples them.
    temp=bmesh.new()
    temp.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(temp,faces=list(temp.faces))
    temp.to_mesh(mesh)
    temp.free()
    ob=bpy.data.objects.new('REFINED_TrouserUnion',mesh)
    bpy.context.scene.collection.objects.link(ob)
    previous_active=bpy.context.view_layer.objects.active
    previous_selection=list(bpy.context.selected_objects)
    try:
        for old in previous_selection:
            old.select_set(False)
        ob.select_set(True)
        bpy.context.view_layer.objects.active=ob
        remesh=ob.modifiers.new('ConnectedTrouserSurface','REMESH')
        remesh.mode='VOXEL'
        remesh.voxel_size=spacing
        remesh.use_smooth_shade=True
        bpy.ops.object.modifier_apply(modifier=remesh.name)
        smoothing=ob.modifiers.new('SoftTrouserJoin','SMOOTH')
        smoothing.factor=.75
        smoothing.iterations=4
        bpy.ops.object.modifier_apply(modifier=smoothing.name)
        dense_tris=sum(len(f.vertices)-2 for f in ob.data.polygons)
        decimated=not uniform and dense_tris>budget
        if decimated:
            decimate=ob.modifiers.new('TrouserSurfaceBudget','DECIMATE')
            decimate.ratio=max(.001,min(1.,(budget-4)/dense_tris))
            decimate.use_collapse_triangulate=True
            bpy.ops.object.modifier_apply(modifier=decimate.name)
        result_mesh=ob.data
        result_mesh.update()
        if not result_mesh.vertices or not result_mesh.polygons:
            raise RuntimeError('Trouser union returned empty geometry; source preserved')
        count=_components(result_mesh)
        if count!=1:
            raise RuntimeError(f'Trouser union has {count} connected components; source preserved')
        tris=sum(len(f.vertices)-2 for f in result_mesh.polygons)
        if tris>budget:
            raise RuntimeError(f'Trouser budget exceeded: {tris} > {budget}; source preserved')
        output_weights=[_weights(v.co,p,s) for v in result_mesh.vertices]
        output_faces=[]
        for f in result_mesh.polygons:
            _,nearest,_=tree.find(f.center)
            output_faces.append((list(f.vertices),colors[original[nearest]]))
        # All observable validation above happens before mutating the Mesher.
        new=[]
        for v,weights in zip(result_mesh.vertices,output_weights):
            nv=m.bm.verts.new(v.co)
            m.weights.append((nv,weights))
            new.append(nv)
        for indices,uv in output_faces:
            nf=m.bm.faces.new([new[i] for i in indices])
            nf.smooth=True
            for loop in nf.loops:
                loop[m.uv].uv=uv
        bmesh.ops.delete(m.bm,geom=list(chosen),context='VERTS')
        m.weights=[(v,w) for v,w in m.weights if v.is_valid]
        for tag,vertices in m.tags.items():
            m.tags[tag]=[v for v in vertices if v.is_valid]
        m.tags['pants']=new
        m.tags['fused_pants']=new
        m.pants_fusion_report=dict(source_vertices=len(original),source_triangles=original_tris,
            voxel_size_m=spacing,smooth_iterations=4,dense_triangles=dense_tris,decimated=decimated,
            output_vertices=len(new),output_triangles=tris,triangle_budget=budget,
            connected_components=count,max_influences=max(map(len,output_weights)),
            max_weight_sum_error=max(abs(sum(w.values())-1.) for w in output_weights))
        print('REFINED_PANTS_FUSED',m.pants_fusion_report,flush=True)
        return m.pants_fusion_report
    finally:
        result_mesh=ob.data
        bpy.data.objects.remove(ob,do_unlink=True)
        if result_mesh.users==0:
            bpy.data.meshes.remove(result_mesh)
        if mesh!=result_mesh and mesh.users==0:
            bpy.data.meshes.remove(mesh)
        for old in previous_selection:
            if old.name in bpy.context.view_layer.objects:
                old.select_set(True)
        if previous_active and previous_active.name in bpy.context.view_layer.objects:
            bpy.context.view_layer.objects.active=previous_active
