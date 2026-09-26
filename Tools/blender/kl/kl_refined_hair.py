"""Reference-led hair, songkok and tudung for the refined KL cast.

Family outlines follow the approved front/side sheets. Hidden fold structure and
NPC hair variants are inferred. Everything shares the existing Head/Chest rig.
"""
import math
from mathutils import Vector

TAU = math.tau


def make_hair(m, p, s):
    # Imported lazily so kl_refined can delegate here without an import cycle.
    import kl_refined as R
    rx, ry, rz, hz = s['headrx'], s['headry'], s['headrz'], s['headc']
    style = s['hair']
    color = 'grey_hair' if s.get('grey') else 'hair'
    weights = {'Head': 1.0}
    original = set(m.bm.verts)

    def lock(control, width, depth, n=22):
        points = R.bezier(control, n)
        radii = []
        for k in range(n):
            t = k/(n-1)
            envelope = (.055 + .945 * math.sin(math.pi*t)**.67)
            radii.append((width*envelope, depth*envelope))
        R.tube(m, points, radii, color, weights, sides=14)

    def scalp(crown, side_radius=1.045, depth_radius=1.045, fringe=.98):
        # One continuous cap, with a swept hairline and a low nape.
        seg, nr = 64, 24
        rings = []
        top = R.vertex(m, (0, .012, crown), weights)
        for i in range(1,nr+1):
            ring = []
            for j in range(seg):
                a = j/seg*TAU
                front = max(0,math.cos(a))
                end = R.mix(2.02, fringe + .10*math.sin(a), front**3)
                phi = end*i/nr
                wave = 1 + .012*math.cos(5*a + 1.2*phi)*math.sin(phi)**2
                zz = hz + (crown-hz)*math.cos(phi)
                if phi > math.pi/2:
                    zz = hz + rz*1.045*math.cos(phi)
                ring.append(R.vertex(m, (rx*side_radius*math.sin(phi)*math.sin(a)*wave,
                    .012-ry*depth_radius*math.sin(phi)*math.cos(a)*wave, zz), weights))
            rings.append(ring)
        for j in range(seg):
            R.face(m,[top,rings[0][j],rings[0][(j+1)%seg]],color)
        for a,b in zip(rings,rings[1:]):
            for j in range(seg):
                R.face(m,[a[j],a[(j+1)%seg],b[(j+1)%seg],b[j]],color)

    def finish_height():
        # Hair owns total height, leaving the calibrated face and skull untouched.
        new = set(m.bm.verts)-original
        top = max(v.co.z for v in new)
        scale = (s['height']-hz)/max(.001,top-hz)
        for v in new:
            if v.co.z > hz:
                v.co.z = hz + (v.co.z-hz)*scale

    if style == 'tudung':
        make_tudung(m,p,s)
        finish_height()
        return

    if style in ('songkok','police'):
        scalp(hz+rz*1.03,fringe=.94)
        top=s['height']; low=top-(.144 if style=='songkok' else .12)
        # The approved songkok is almost straight sided, with a soft top edge.
        rows=[(low,rx*.824,ry*.94,.008),(low+.004,rx*.840,ry*.956,.008),
              (low+.015,rx*.843,ry*.956,.008),(top-.013,rx*.818,ry*.91,.004),
              (top-.004,rx*.811,ry*.906,.004),(top,rx*.802,ry*.887,.004)]
        R.loft(m,rows,'songkok' if style=='songkok' else 'police_blue',weights,seg=64,step=.009)
        if style=='police':
            m.ball((0,-ry*.88,low+.017),(rx*.82,ry*.67,.015),'suit_black',seg=32,rings=12,bones=weights)
            m.ball((0,-ry*.956,low+.071),(.027,.006,.032),'gold',seg=20,rings=14,bones=weights)
        return

    if style == 'curly':
        # Union of asymmetric broad curl masses. A light voxel fusion removes
        # intersecting bead seams while keeping the approved rounded curl outlines.
        import bpy
        import bmesh
        core=R.K.Mesher(smooth=True)
        core.ball((0,.010,hz+.108),(rx*1.06,ry*1.07,rz*.98),color,seg=36,rings=24,bones=weights)
        curl_specs=[
            (-.97,-.16,.55,.43,.48,.43,-.30),(.99,-.15,.58,.46,.47,.48,.34),
            (-.71,-.75,1.10,.47,.44,.40,-.38),(.64,-.76,1.16,.47,.43,.40,.30),
            (-.35,-.39,1.25,.51,.48,.48,-.43),(.37,-.20,1.36,.49,.51,.48,.28),
            (-.98,.47,.93,.43,.47,.45,-.28),(.99,.42,1.00,.44,.44,.41,.36),
            (-.57,.91,.88,.45,.42,.46,.35),(.55,.97,.92,.48,.44,.49,-.33),
            (-.13,.88,1.26,.47,.48,.43,-.14),(-.06,-.99,1.07,.31,.30,.34,.12),
            (-.91,.40,.15,.34,.38,.36,.25),(.84,.56,.18,.36,.38,.34,-.2),
            (-.29,.98,.34,.38,.40,.37,-.2),(.27,1.0,.37,.40,.39,.36,.2),
        ]
        for x,y,z,wx,wy,wz,tilt in curl_specs:
            core.ball((x*rx,y*ry+.010,hz+z*rz),(rx*wx,ry*wy,rz*wz),color,
                      rot=(.10,tilt,.12*tilt),seg=28,rings=20,bones=weights)
        mesh=bpy.data.meshes.new('REVIEW_CurlUnionSource')
        core.bm.to_mesh(mesh);core.bm.free()
        ob=bpy.data.objects.new('REVIEW_CurlUnionSource',mesh)
        bpy.context.scene.collection.objects.link(ob)
        mod=ob.modifiers.new('FuseCurlMasses','REMESH');mod.mode='VOXEL';mod.voxel_size=.006
        mod.use_smooth_shade=True
        smooth=ob.modifiers.new('SoftenFusedSeams','SMOOTH');smooth.factor=.62;smooth.iterations=3
        dec=ob.modifiers.new('CurlDeliveryDensity','DECIMATE');dec.ratio=s.get('hair_density',.66)
        bpy.context.view_layer.update()
        dg=bpy.context.evaluated_depsgraph_get()
        fused=bpy.data.meshes.new_from_object(ob.evaluated_get(dg),depsgraph=dg)
        mapping=[R.vertex(m,v.co,weights) for v in fused.vertices]
        for poly in fused.polygons:R.face(m,[mapping[i] for i in poly.vertices],color)
        bpy.data.objects.remove(ob,do_unlink=True)
        bpy.data.meshes.remove(mesh);bpy.data.meshes.remove(fused)
        finish_height()
        return
    if style == 'pigtails':
        scalp(s['height'],side_radius=1.04,depth_radius=1.075,fringe=.90)
        # Broad lens-shaped swept fringes; the surface lies close to the scalp.
        lock([(-.018,-.026,s['height']-.003),(-rx*.54,-ry*.75,hz+rz*1.13),
              (-rx*.87,-ry*.85,hz+rz*.48),(-rx*.93,-ry*.43,hz-rz*.04)],rx*.29,ry*.12,26)
        lock([(-.01,-.025,s['height']-.004),(rx*.35,-ry*.81,hz+rz*1.11),
              (rx*.76,-ry*.92,hz+rz*.52),(rx*.90,-ry*.56,hz+rz*.04)],rx*.33,ry*.125,26)
        lock([(rx*.03,-ry*.42,s['height']-.033),(rx*.10,-ry*.96,hz+rz*.85),
              (rx*.35,-ry*1.04,hz+rz*.58)],rx*.17,ry*.07,22)
        for sign in (-1,1):
            knot=Vector((sign*rx*1.03,.018,hz+rz*.52))
            # Tails are compact curved tufts, broad near the tie and sharply tapered.
            pts=R.bezier([knot+Vector((sign*.016,.01,-.002)),
                knot+Vector((sign*.112,.02,-.008)),
                knot+Vector((sign*.113,.018,-.091)),
                knot+Vector((sign*.070,-.005,-.143))],26)
            radii=[]
            for i in range(26):
                t=i/25; env=(.32+.68*math.sin(math.pi*t)**.58)*(1-.9*t**5)
                radii.append((rx*.265*env,ry*.245*env))
            R.tube(m,pts,radii,color,weights,sides=20)
            m.ball(knot,(.032,.028,.043),'cord_red',seg=26,rings=18,bones=weights)
            # A thin temple strand is a tapered curved lock, never a rectangular strip.
            lock([(sign*rx*.91,-ry*.27,hz+rz*.35),(sign*rx*.98,-ry*.42,hz-rz*.20),
                  (sign*rx*.90,-ry*.35,hz-rz*.56)],rx*.045,ry*.035,18)
        finish_height()
        return

    # Reusable swept silhouettes for Aiman, Mei, Ravi and the neighbourhood kid.
    scalp(s['height']-.012,side_radius=1.045,depth_radius=1.045,fringe=.95)
    for idx in range(4):
        x=(-.64+idx*.38)*rx
        lock([(x,-ry*.85,hz+rz*.56),(x+rx*.40,-ry*.78,s['height']-.012),
              (x+rx*.62,ry*.10,s['height']-.025),(x+rx*.38,ry*.60,hz+rz*.61)],
             rx*(.24 if style=='sidepart' else .28),ry*.095,26)
    if style=='bun':
        m.ball((0,ry*1.02,hz+rz*.48),(rx*.51,ry*.52,rz*.47),color,seg=32,rings=22,bones=weights)
    finish_height()


def make_tudung(m,p,s):
    import kl_refined as R
    rx,ry,rz,hz=s['headrx'],s['headry'],s['headrz'],s['headc']
    col=s['scarf']; weights={'Head':1.0}
    seg,nr=64,26
    rings=[]
    # Ellipsoidal shell with a true front opening. The old opening derived Y only
    # from X, so its forehead edge ran through the skull. Here all three axes agree.
    for i in range(nr):
        t=i/nr; row=[]
        for j in range(seg):
            a=TAU*j/seg; up=math.sin(a)
            opening=1.26-.09*max(0,up)+.20*max(0,-up)
            theta=R.mix(opening,math.pi,t)
            expansion=R.smooth(min(1,t*5))
            radx=rx*(1.005+.105*expansion)
            radz=rz*(1.018+((s['height']-hz)/rz-1.018)*expansion)
            rady=ry*(1.045+.20*expansion)
            xx=radx*math.sin(theta)*math.cos(a)
            zz=hz+radz*math.sin(theta)*math.sin(a)-.005*(1-t)
            yy=-rady*math.cos(theta)-.009*(1-t)
            # Draped, shallow vertical valleys break the otherwise helmet-like hood.
            ripple=.004*math.sin(6*a+1.1)*math.sin(math.pi*t)**2
            xx+=ripple*math.cos(a)
            yy+=.006*math.cos(3*a)*math.sin(math.pi*t)**2
            row.append(R.vertex(m,(xx,yy,zz),weights,'scarf_hood'))
        rings.append(row)
    for a,b in zip(rings,rings[1:]):
        for j in range(seg):R.face(m,[a[j],a[(j+1)%seg],b[(j+1)%seg],b[j]],col)
    pole=R.vertex(m,(0,ry*1.245,hz),weights,'scarf_hood')
    for j in range(seg):R.face(m,[rings[-1][j],rings[-1][(j+1)%seg],pole],col)
    rim=[v.co.copy() for v in rings[0]]+[rings[0][0].co.copy()]
    R.tube(m,rim,[.0035]*len(rim),col,weights,sides=8,caps=False)

    # Curved shoulder cowl: a rounded front point, longer rear drape and two soft
    # radial folds. The shoulder edges remain high rather than making a flat bib.
    drape=[]
    for i in range(21):
        t=i/20; row=[]
        for j in range(seg):
            a=j/seg*TAU; front=math.cos(a); cf=abs(front)
            lower=s['sz']+.028-.245*cf**1.35-.055*max(0,-front)**2
            top=s['chin']+.023-.013*max(0,-front)
            spread=1-(1-t)**3
            rad=R.mix(rx*.55,s['bodyw']*.98,spread)
            dep=R.mix(ry*.75,s['bodyd']*1.14,t**.70)
            fold=(.015*math.exp(-((t-.38)/.16)**2)+.013*math.exp(-((t-.72)/.14)**2))*cf**1.4
            x=rad*math.sin(a)
            y=-(dep+fold)*math.cos(a)-.014
            z=R.mix(top,lower,t)-.012*math.sin(math.pi*t)*cf+.004*math.sin(3*a)*math.sin(math.pi*t)
            hw=1-R.smooth((t-.08)/.53)
            wg={'Head':hw,'Chest':1-hw}
            row.append(R.vertex(m,(x,y,z),wg,'scarf_cowl'))
        drape.append(row)
    for a,b in zip(drape,drape[1:]):
        for j in range(seg):R.face(m,[a[j],a[(j+1)%seg],b[(j+1)%seg],b[j]],col)
    # A folded hem has thickness and catches light; it shares the chest weights.
    hem=[v.co.copy() for v in drape[-1]]+[drape[-1][0].co.copy()]
    R.tube(m,hem,[.0032]*len(hem),col,{'Chest':1},sides=8,caps=False)

    # The rear veil continues from the rounded head hood to the back point. Its
    # top overlaps the hood outside the skull; its lower portion covers the cowl,
    # so neither the neck nor a floating bowl-shaped collar appears in profile.
    # Width and drop follow the approved back/side sheet; the hidden inner fold is inferred.
    veil=[]
    for i in range(25):
        t=i/24; row=[]
        for j in range(41):
            u=-1+2*j/40
            topz=hz+rz*(.50-.25*u*u)
            bottomz=s['sz']-.27+.285*abs(u)**1.45
            span=R.mix(rx*1.01,s['bodyw']*.97,R.smooth(t))
            xx=span*u
            topx=rx*1.01*u
            hood_depth=ry*1.245*math.sqrt(max(.015,1-(topx/(rx*1.11))**2-((topz-hz)/(s['height']-hz))**2))-.022
            bottom_depth=s['bodyd']*1.20*math.sqrt(max(.04,1-.96*u*u))+.008
            yy=R.mix(hood_depth,bottom_depth,t)
            zz=R.mix(topz,bottomz,t)
            # Two restrained folds converge toward the back point.
            yy+=.006*math.cos(3*math.pi*u)*(math.sin(math.pi*t)**1.2)
            hw=1-R.smooth(t/.78)
            row.append(R.vertex(m,(xx,yy,zz),{'Head':hw,'Chest':1-hw},'scarf_veil'))
        veil.append(row)
    for a,b in zip(veil,veil[1:]):
        for j in range(40):R.face(m,[a[j],a[j+1],b[j+1],b[j]],col)
    edge=[v.co.copy() for v in veil[-1]]
    R.tube(m,edge,[.0031]*len(edge),col,{'Chest':1},sides=8,caps=True)


def orient_scarf_normals(m,s):
    """Run after global normal recalculation: open cloth has no closed volume sign."""
    center=Vector((0,0,s['headc']))
    groups={tag:set(m.tags.get(tag,[])) for tag in ('scarf_hood','scarf_cowl','scarf_veil')}
    for f in m.bm.faces:
        co=f.calc_center_median()
        for tag,verts in groups.items():
            if verts and all(v in verts for v in f.verts):
                desired=(co-center) if tag=='scarf_hood' else (Vector((0,1,0)) if tag=='scarf_veil' else Vector((co.x,co.y,0)))
                if f.normal.dot(desired)<0:f.normal_flip()
                break
