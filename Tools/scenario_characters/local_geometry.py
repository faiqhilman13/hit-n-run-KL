"""Four local Pak Mat sculpt studies, without modifying the game asset builders.

Public API: build_variant('A'|'B'|'C'|'D') returns objects, roles, metadata.
The scene is reset. All objects are static meshes in metres, Z up, -Y front.
The reference concepts guide design language, not exact reconstruction. Head-count
targets, concealed anatomy, hands, rear surfaces and fabric drape are inferred.
There is no animation/retopology/game-readiness claim for these comparison studies.
"""
from pathlib import Path
import math
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent.parent
sys.path.insert(0, str(PROJECT / 'Tools/blender/kl'))
import kl_core as K
import kl_refined as R

TAU = math.tau
SPECS = {
    'A': dict(label='Warm animated film', heads=4.5, rx=.173, rz=1.66/9, ry=.146,
              shoulder_z=1.112, shoulder_x=.222, belly_w=.292, belly_d=.247, hem=.660,
              eye_x=.040, eye_z=.024, eye_open=1., cheek=.022, nose=.066,
              exponent=.98, jaw=.12, arm_up=.224, arm_lo=.214, hand=1., foot=.280,
              foot_x=.157, wrap_low=.164, brow=.009, moustache=.018, arm_r=.073),
    'B': dict(label='Graphic comic', heads=4.75, rx=.166, rz=1.66/9.5, ry=.135,
              shoulder_z=1.153, shoulder_x=.239, belly_w=.277, belly_d=.216, hem=.708,
              eye_x=.032, eye_z=.033, eye_open=1., cheek=.012, nose=.076,
              exponent=.76, jaw=.16, arm_up=.239, arm_lo=.224, hand=1.02, foot=.272,
              foot_x=.153, wrap_low=.174, brow=.011, moustache=.021, arm_r=.068),
    'C': dict(label='Handmade miniature', heads=4., rx=.207, rz=1.66/8, ry=.164,
              shoulder_z=1.061, shoulder_x=.241, belly_w=.327, belly_d=.268, hem=.621,
              eye_x=.047, eye_z=.029, eye_open=.43, cheek=.027, nose=.076,
              exponent=1.02, jaw=.13, arm_up=.207, arm_lo=.197, hand=1.11, foot=.300,
              foot_x=.178, wrap_low=.152, brow=.011, moustache=.023, arm_r=.081),
    'D': dict(label='Grounded stylized', heads=5.75, rx=.133, rz=1.66/11.5, ry=.125,
              shoulder_z=1.209, shoulder_x=.222, belly_w=.260, belly_d=.213, hem=.792,
              eye_x=.027, eye_z=.0135, eye_open=.87, cheek=.010, nose=.049,
              exponent=.95, jaw=.08, arm_up=.266, arm_lo=.235, hand=.90, foot=.261,
              foot_x=.149, wrap_low=.206, brow=.0057, moustache=.0118, arm_r=.064),
}
COLORS = dict(skin=(.59,.29,.12), batik=(.62,.20,.067), sarong=(.06,.15,.085),
    hair=(.024,.018,.015), songkok=(.012,.013,.012), eye_white=(.91,.85,.72),
    iris=(.11,.055,.026), pupil=(.004,.003,.002), mouth=(.20,.065,.035),
    teeth=(.90,.83,.66), sandal=(.17,.071,.025), sole=(.05,.026,.015), nail=(.66,.36,.20))


def _mesher(): return K.Mesher(detail=1., smooth=True, lsegs=3)
def _v(m, co): return R.vertex(m, co, {})
def _f(m, vertices): R.face(m, vertices, 'skin')
def _lerp(a,b,t): return a+(b-a)*t
def _gauss(x, c, w): return math.exp(-((x-c)/w)**2)
def _signed_power(x,p): return math.copysign(abs(x)**p,x)


def _placeholder(role):
    name='StudyPreview_'+role
    mat=bpy.data.materials.get(name)
    if mat: return mat
    mat=bpy.data.materials.new(name)
    mat.diffuse_color=(*COLORS.get(role,COLORS['skin']),1)
    mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value=mat.diffuse_color
    bs.inputs['Roughness'].default_value=.7
    return mat


def _uv(ob, low=0., high=1.66):
    """Continuous garment coordinates: front U=.5, back seam; V is height."""
    uv=ob.data.uv_layers.active or ob.data.uv_layers.new(name='UVMap')
    for poly in ob.data.polygons:
        us=[]
        for vi in poly.vertices:
            co=ob.data.vertices[vi].co
            us.append(.5+math.atan2(co.x,-co.y)/TAU)
        seam=max(us)-min(us)>.5
        for li,u in zip(poly.loop_indices,us):
            if seam and u<.5:u+=1.
            z=ob.data.vertices[ob.data.loops[li].vertex_index].co.z
            uv.data[li].uv=(u,(z-low)/max(.001,high-low))
    ob['uv_contract']='U=circumference, front0.5; V=height within garment; back seam'
    ob['uv_low_m']=low;ob['uv_high_m']=high


def _emit(m, name, role, union=None, smooth=3, uv=None):
    bad=[f for f in m.bm.faces if f.calc_area()<1e-12]
    if bad:bmesh.ops.delete(m.bm,geom=bad,context='FACES_ONLY')
    bmesh.ops.recalc_face_normals(m.bm,faces=list(m.bm.faces))
    ob,_,_=m.to_object(name,_placeholder(role))
    if union:
        bpy.ops.object.select_all(action='DESELECT')
        ob.select_set(True);bpy.context.view_layer.objects.active=ob
        md=ob.modifiers.new('SculpturalUnion','REMESH');md.mode='VOXEL'
        md.voxel_size=union;md.use_smooth_shade=True
        bpy.ops.object.modifier_apply(modifier=md.name)
        md=ob.modifiers.new('SculpturalRelax','SMOOTH');md.factor=.58;md.iterations=smooth
        bpy.ops.object.modifier_apply(modifier=md.name)
    for f in ob.data.polygons:f.use_smooth=True
    _uv(ob,*(uv or (0.,1.66)))
    ob['role']=role;ob['static_study']=True
    return ob


def _ball(m,center,radii,seg=32,rings=20):
    m.ball(center,radii,'skin',seg=seg,rings=rings)


def _tube(m, points, radii, sides=24, caps=True):
    R.tube(m,points,radii,'skin',{},sides=sides,caps=caps)


def _curve(m,points,radii,n=24,sides=16):
    points=R.bezier(points,n)
    if isinstance(radii,(float,int)):radii=[radii]*n
    elif len(radii)!=n:
        src=radii
        radii=[_lerp(src[0],src[-1],i/(n-1)) for i in range(n)]
    _tube(m,points,radii,sides)


def _face_shape(s):
    rx,ry,rz=s['rx'],s['ry'],s['rz'];cz=1.547-rz;s['headc']=cz
    def ring_width(t):
        return rx*math.sqrt(max(0,1-t*t))*(1+s['jaw']*_gauss(t,-.42,.36)-.02*max(0,-t))
    def front(x,z,lift=0.):
        t=max(-.99999,min(.99999,(z-cz)/rz))
        rw=max(.0001,ring_width(t))
        q=max(-.999999,min(.999999,x/rw))
        a=math.asin(_signed_power(q,1/s['exponent']))
        base=-ry*math.sqrt(1-t*t)*abs(math.cos(a))**s['exponent']
        # Cheek, muzzle, bridge, tip and chin are part of the same skin surface.
        cheek=s['cheek']*(_gauss(x,rx*.51,rx*.36)+_gauss(x,-rx*.51,rx*.36))*_gauss(t,-.24,.35)
        bridge=.018*_gauss(x,0,rx*.18)*_gauss(t,.22,.43)
        if s['variant']=='B':
            tip=s['nose']*max(0.,1-abs(x)/(rx*.30))*max(0.,1-abs(t+.12)/.38)
        else:tip=s['nose']*math.exp(-(x/(rx*.25))**4)*_gauss(t,-.11,.235)
        chin=.014*_gauss(x,0,rx*.64)*_gauss(t,-.68,.22)
        brow=.007*(_gauss(x,rx*.40,rx*.31)+_gauss(x,-rx*.40,rx*.31))*_gauss(t,.42,.19)
        return Vector((x,base-cheek-bridge-tip-chin-brow-lift,z))
    m=_mesher();rings=[];seg=96;n=68
    for i in range(1,n):
        t=-math.cos(math.pi*i/n);z=cz+rz*t;rw=ring_width(t);ring=[]
        for k in range(seg):
            a=TAU*k/seg;x=rw*_signed_power(math.sin(a),s['exponent'])
            if math.cos(a)>=0:
                co=front(x,z)
            else:co=Vector((x,ry*math.sqrt(1-t*t)*abs(math.cos(a))**s['exponent'],z))
            ring.append(_v(m,co))
        rings.append(ring)
    for i in range(len(rings)-1):
        for k in range(seg):_f(m,[rings[i][k],rings[i][(k+1)%seg],rings[i+1][(k+1)%seg],rings[i+1][k]])
    for row,z in ((rings[0],cz-rz),(rings[-1],cz+rz)):
        tip=_v(m,(0,0,z))
        for k in range(seg):_f(m,[tip,row[k],row[(k+1)%seg]])
    chin=cz-rz
    _tube(m,[(0,.015,s['shoulder_z']-.060),(0,.006,chin+.095)],[(rx*.59,ry*.66),(rx*.47,ry*.50)],32)
    for sign in (-1,1):
        _ball(m,(sign*rx*.99,-.002,cz-rz*.01),(rx*.21,ry*.235,rz*.285))
    head=_emit(m,s['variant']+'_head_neck_ears','skin',union=.0026,smooth=2)
    return head,front


def _patch(m,point_at,cx,cz,ax,az,upper=1.,lower=1.,rings=10,seg=48):
    # A radial surface with distinct upper/lower arcs gives expressive almond lids.
    loops=[]
    for i in range(1,rings+1):
        r=i/rings;row=[]
        for k in range(seg):
            a=TAU*k/seg;yy=math.sin(a)
            x=cx+ax*r*math.cos(a)
            z=cz+az*r*yy*(upper if yy>=0 else lower)
            row.append(_v(m,point_at(x,z,r)))
        loops.append(row)
    v=_v(m,point_at(cx,cz,0))
    for k in range(seg):_f(m,[v,loops[0][k],loops[0][(k+1)%seg]])
    for i in range(len(loops)-1):
        for k in range(seg):_f(m,[loops[i][k],loops[i+1][k],loops[i+1][(k+1)%seg],loops[i][(k+1)%seg]])


def _features(s,front):
    out=[];rx,ry,rz,cz=s['rx'],s['ry'],s['rz'],s['headc'];key=s['variant']
    white,iris,pupil,lids,brows,inner=_mesher(),_mesher(),_mesher(),_mesher(),_mesher(),_mesher()
    ez=cz+rz*.245;ex=rx*.405;ax,az=s['eye_x'],s['eye_z']
    iris_r=ax*(.43 if key=='D' else .42)
    for sign in (-1,1):
        cx=sign*ex;this_z=ez+(.002 if sign>0 and key in ('A','C') else 0)
        up=s['eye_open'];lo=.74 if key!='B' else .94
        def eye_surface(x,z,r=0):
            r2=min(1.,((x-cx)/ax)**2+((z-this_z)/(az*.95))**2)
            return front(x,z,.004+.010*(1-r2))
        _patch(white,eye_surface,cx,this_z,ax,az,up,lo)
        icx=cx-sign*.002;icz=this_z-az*(.17 if key=='C' else .035)
        def clipped_eye(x,z,lift):
            # Irises disappear behind the actual aperture, especially C's
            # sleepy lid, rather than being painted across the upper eyelid.
            q=max(-.9999,min(.9999,(x-cx)/ax));edge=math.sqrt(1-q*q)
            z=max(this_z-az*lo*edge+.0001,min(this_z+az*up*edge-.0001,z))
            return eye_surface(x,z)-Vector((0,lift,0))
        def iris_surface(x,z,r):return clipped_eye(x,z,.0017)
        _patch(iris,iris_surface,icx,icz,iris_r,iris_r*.96,rings=7,seg=40)
        def pupil_surface(x,z,r):return clipped_eye(x,z,.0032)
        _patch(pupil,pupil_surface,icx,icz,iris_r*.51,iris_r*.55,rings=6,seg=36)
        # Eyelid rims and the crease above them belong to skin, not eyeliner.
        for upper in (True,False):
            pts=[]
            for i in range(41):
                a=math.pi*i/40+(0 if upper else math.pi)
                z=this_z+az*math.sin(a)*(up if upper else lo)
                pts.append(front(cx+ax*math.cos(a),z,.0065))
            rs=[(.0038 if upper else .0024)*(.6+.4*math.sin(math.pi*i/40)) for i in range(41)]
            _tube(lids,pts,rs,12)
        # Broader sleepy lid pad above C's reduced aperture.
        if key=='C':
            pts=[front(cx+ax*math.cos(math.pi*i/40),this_z+az*(.21+.58*math.sin(math.pi*i/40)),.004) for i in range(41)]
            _tube(lids,pts,[.0065]*41,14)
        bz=this_z+az*up+.018
        pts=[front(cx+ax*_lerp(-1.05,1.05,t),bz+.013*math.sin(math.pi*t)+sign*.004*(t-.5),.006) for t in [i/32 for i in range(33)]]
        rs=[s['brow']*(.25+.75*math.sin(math.pi*i/32)**.55) for i in range(33)]
        _tube(brows,pts,rs,14)
        # Inset ear concha; shares skin material and reads from actual depth.
        _ball(inner,(sign*rx*1.048,-ry*.172,cz-rz*.01),(rx*.097,.012,rz*.145),24,16)
    for m,role,name in [(white,'eye_white','sclera'),(iris,'iris','irises'),(pupil,'pupil','pupils'),
                        (lids,'skin','eyelids'),(brows,'hair','eyebrows'),(inner,'skin','ear_concha')]:
        ob=_emit(m,key+'_'+name,role)
        if role=='iris':
            uv=ob.data.uv_layers.active
            for f in ob.data.polygons:
                for li in f.loop_indices:
                    co=ob.data.vertices[ob.data.loops[li].vertex_index].co
                    sign=1 if co.x>0 else -1
                    center_x=sign*ex-sign*.002
                    center_z=ez+(.002 if sign>0 and key in ('A','C') else 0)-az*(.17 if key=='C' else .035)
                    uv.data[li].uv=(.5+.5*(co.x-center_x)/iris_r,.5+.5*(co.z-center_z)/(iris_r*.96))
            ob['uv_contract']='iris disc: center0.5,0.5; radius0.5; clipped behind lids'
            ob['radial_iris_uv']=True
        out.append(ob)
    # A connected upper-lip moustache with a center part and tapered cheek ends.
    m=_mesher();pts=[];rs=[]
    for i in range(65):
        u=-1+2*i/64;x=rx*.61*u
        z=cz-rz*(.33-.06*math.sin(math.pi*abs(u)))
        pts.append(front(x,z,.008+s['moustache']*.32))
        thickness=s['moustache']*(.10+.90*math.sin(math.pi*i/64)**.55)
        rs.append((thickness,thickness*.77))
    _tube(m,pts,rs,20)
    out.append(_emit(m,key+'_moustache','hair'))
    mouth=_mesher();lip=_mesher();mz=cz-rz*.535;mw=rx*.40
    # Small warm smile: restrained, curved lip opening, not a straight dark bar.
    rows=[]
    for j in range(7):
        row=[]
        for i in range(49):
            u=-.999+1.998*i/48
            z=mz+.012*u*u+(.002*u if key=='A' else 0)-.006*(1-u*u)*j/6
            row.append(_v(mouth,front(u*mw,z,.004)))
        rows.append(row)
    for j in range(6):
        for i in range(48):_f(mouth,[rows[j][i],rows[j][i+1],rows[j+1][i+1],rows[j+1][i]])
    pts=[front(mw*u,mz+.012*u*u-.009*(1-u*u),.004) for u in [-1+2*i/40 for i in range(41)]]
    _tube(lip,pts,[.0034*(.3+.7*math.sin(math.pi*i/40)) for i in range(41)],12)
    out.extend([_emit(mouth,key+'_smile','mouth'),_emit(lip,key+'_lower_lip','skin')])
    return out


def _hair_and_hat(s,front):
    rx,ry,rz,cz=s['rx'],s['ry'],s['rz'],s['headc'];key=s['variant'];m=_mesher();rings=[]
    for i in range(22):
        row=[]
        for k in range(96):
            a=TAU*k/96;f=max(0,math.cos(a))
            t0=.04+.72*f**5;t=_lerp(t0,.99,i/21)
            z=cz+rz*t;rr=math.sqrt(1-t*t)
            x=rx*rr*math.sin(a)*1.027;y=-ry*rr*math.cos(a)*1.025
            row.append(_v(m,(x,y,z)))
        rings.append(row)
    for i in range(21):
        for k in range(96):_f(m,[rings[i][k],rings[i+1][k],rings[i+1][(k+1)%96],rings[i][(k+1)%96]])
    hair=_emit(m,key+'_hair','hair')
    m=_mesher()
    # Songkok is an oval textile cap with rounded edge; total height is 1.66m.
    bottom=1.464 if key=='C' else 1.470
    hw=rx*(.88 if key=='C' else .89);hd=ry*.89
    R.loft(m,[(bottom,hw*.99,hd,0),(bottom+.005,hw,hd,0),
              (1.647,hw*.94,hd*.93,.001),(1.657,hw*.91,hd*.90,.001),
              (1.660,hw*.89,hd*.88,.001)],'skin',{},seg=96,step=.004)
    hat=_emit(m,key+'_songkok','songkok')
    return [hair,hat]


def _body(s):
    key=s['variant'];w,d=s['belly_w'],s['belly_d'];sz=s['shoulder_z'];hem=s['hem']
    chin=s['headc']-s['rz'];top=chin-.030;sx=s['shoulder_x']
    rows=[(hem,w*.955,d*.94,-.004),(hem+.035,w,d,-.005),
          (hem+.115,w*1.015,d*1.04,-.009),(sz-.19,w*.94,d*.90,0),
          (sz-.07,w*.85,d*.73,.005),(sz-.002,sx*.98,d*.64,.01),
          (top,.067 if key=='D' else .080,.064,.007)]
    rows=sorted(rows)
    m=_mesher();R.loft(m,rows,'skin',{},seg=80,step=.007,folds=.007)
    arms=[];cuffs=[]
    for sign in (-1,1):
        shoulder=Vector((sign*sx,.007,sz-.008))
        elbow=Vector((sign*(sx+.088),-.004,sz-s['arm_up']))
        wrist=Vector((sign*(sx+.154),-.046,sz-s['arm_up']-s['arm_lo']))
        sleeve_end=shoulder.lerp(elbow,.69)
        # Root is sunk into the yoke, below its sloping shoulder line. Its closed
        # construction cap stays inside the torso instead of peaking above it.
        start=Vector((sign*(sx-.080),.008,sz-.074))
        mid=Vector((sign*(sx+.022),.005,sz-.078))
        points=R.bezier([start,mid,sleeve_end],25)
        cuff_radius=s['arm_r']*1.22
        radii=[_lerp(s['arm_r']*1.38,cuff_radius,i/24) for i in range(25)]
        _tube(m,points,[(r,r*.95) for r in radii],32)
        arms.append((sign,shoulder,elbow,wrist,sleeve_end))
        cuffs.append((sleeve_end,(elbow-shoulder).normalized(),cuff_radius))
    shirt=_emit(m,key+'_batik_shirt','batik',union=.0048,smooth=4,uv=(hem,top))
    # Keep the remeshed garment closed. A fitted upper-chest inset and layered
    # collar describe the V cleanly; deleting voxel faces leaves a ragged edge.
    chest=_mesher();grid=[]
    for j in range(22):
        v=j/21;z=_lerp(top-.094,top+.002,v);half=.050*v+.001
        row=[]
        for i in range(17):
            x=_lerp(-half,half,i/16)
            row.append(_v(chest,R.shell_point(rows,x,z,lift=.010)))
        grid.append(row)
    for j in range(21):
        for i in range(16):_f(chest,[grid[j][i],grid[j+1][i],grid[j+1][i+1],grid[j][i+1]])
    chest_ob=_emit(chest,key+'_upper_chest_inset','skin')
    # Small tapered collar leaves follow the chest instead of forming two large
    # bow-tie panels. Their points remain separated around the clean skin V.
    collar=_mesher()
    for sign in (-1,1):
        xz=[(sign*.040,top+.004),(sign*.088,top-.024),(sign*.061,top-.101)]
        corners=[Vector((x,z)) for x,z in xz]
        grid={};n=15
        for i in range(n+1):
            for j in range(n+1-i):
                a,b=i/n,j/n;c=1-a-b
                x,z=corners[0]*a+corners[1]*b+corners[2]*c
                pt=R.shell_point(rows,x,z,lift=.009+.010*a*b*c*27)
                grid[i,j]=_v(collar,pt)
        for i in range(n):
            for j in range(n-i):
                _f(collar,[grid[i,j],grid[i+1,j],grid[i,j+1]])
                if i+j<n-1:_f(collar,[grid[i+1,j],grid[i+1,j+1],grid[i,j+1]])
    collar_ob=_emit(collar,key+'_batik_collar','batik_trim',uv=(hem,top))
    md=collar_ob.modifiers.new('FoldedCollarThickness','SOLIDIFY');md.thickness=.003;md.offset=0
    bpy.context.view_layer.objects.active=collar_ob;bpy.ops.object.modifier_apply(modifier=md.name)
    buttons=_mesher()
    for j in range(4):
        z=_lerp(hem+.068,top-.18,j/3);co=R.shell_point(rows,0,z,lift=.007)
        _ball(buttons,co,(.006,.004,.006),18,12)
    out=[shirt,chest_ob,collar_ob,_emit(buttons,key+'_buttons','sandal')]
    # Resting arms and hands are fused per side, including thumb web and unequal fingers.
    for sign,shoulder,elbow,wrist,sleeve_end in arms:
        m=_mesher();f=s['hand'];ar=s['arm_r']
        # Start concealed inside the cuff. A complete raised deltoid capsule is
        # unnecessary in a static clothed study and can poke through its yoke.
        arm_start=sleeve_end.lerp(shoulder,.16)
        points=R.bezier([arm_start,arm_start.lerp(elbow,.48),elbow,wrist],49)
        radii=[]
        for i in range(49):
            t=i/48
            r=ar*(1-.53*t+.07*math.sin(TAU*t))
            radii.append((r,r*.90))
        hc=wrist+Vector((sign*.013,-.002,-.042*f))
        # Continue the forearm inside the palm before closing the tube. Ending
        # it at the wrist exposed a flat circular cap beyond the thinner hand,
        # which read as a dark cuff/ball-joint seam in profile.
        palm_interior=hc+Vector((0,.001,-.008*f))
        ra,rb=radii[-1]
        for i in range(1,13):
            t=i/12;q=t*t*(3-2*t)
            points.append(wrist.lerp(palm_interior,t))
            radii.append((_lerp(ra,.024*f,q),_lerp(rb,.017*f,q)))
        _tube(m,points,radii,28)
        # Flatter metacarpal plane and shorter palm; unequal flexed fingers carry
        # the silhouette instead of terminating a long capsule-shaped mitten.
        _ball(m,hc,(.046*f,.025*f,.052*f),32,24)
        # Four fingers, gently cupped in depth. Inner=index, outer=pinky.
        for offset,length,radius in [(-.030,.083,.014),(-.009,.094,.0145),(.013,.086,.0133),(.033,.067,.0118)]:
            base=hc+Vector((sign*offset,-.001,-.026*f))
            curl=.023+.013*(offset/.033)
            pts=R.bezier([base,base+Vector((sign*.007,-.001,-length*.40*f)),
                          base+Vector((sign*.009,-curl*.67,-length*.90*f)),
                          base+Vector((sign*.003,-curl,-length*f))],19)
            rs=[radius*f*(1-.43*(i/18)**2) for i in range(19)]
            _tube(m,pts,rs,16)
            _ball(m,pts[-1],(rs[-1],rs[-1],rs[-1]),16,12)
        # Broad thumb root supplies an anatomical web; tip curls toward the palm.
        t0=hc+Vector((-sign*.030,-.010,.018*f))
        _ball(m,t0,(.024*f,.022*f,.038*f),24,18)
        pts=R.bezier([t0,t0+Vector((-sign*.035,-.022,-.012)),
                      t0+Vector((-sign*.033,-.030,-.063)),t0+Vector((-sign*.015,-.027,-.074))],23)
        _tube(m,pts,[.018*f*(1-.43*i/22) for i in range(23)],18)
        _ball(m,pts[-1],(.010*f,.010*f,.011*f),18,12)
        out.append(_emit(m,key+('_left_arm_hand' if sign>0 else '_right_arm_hand'),'skin',union=.0026,smooth=3))
    return out


def _sarong(s):
    key=s['variant'];w=s['belly_w']*.97;d=s['belly_d']*.92
    low,high=s['wrap_low'],s['hem']+.065
    m=_mesher();rings=[];seg=112;nr=58
    def at(a,v,offset=0):
        z=_lerp(low,high,v)
        fold=(.007 if key=='D' else .010)*(math.sin(5*a+.4)+.42*math.sin(9*a-2*v))*(1-.55*v)
        rx=w*(.89+.10*v)+fold;ry=d*(.84+.16*v)+fold
        zz=z+.009*math.sin(a+.4)*(1-v)**3
        return Vector(((rx+offset)*math.sin(a),-(ry+offset)*math.cos(a),zz))
    for j in range(nr+1):
        rings.append([_v(m,at(TAU*k/seg,j/nr)) for k in range(seg)])
    for j in range(nr):
        for k in range(seg):_f(m,[rings[j][k],rings[j+1][k],rings[j+1][(k+1)%seg],rings[j][(k+1)%seg]])
    main=_emit(m,key+'_sarong_wrap','sarong',uv=(low,high))
    md=main.modifiers.new('CottonWrapThickness','SOLIDIFY');md.thickness=.0035;md.offset=-1
    bpy.context.view_layer.objects.active=main;bpy.ops.object.modifier_apply(modifier=md.name)
    # Overlap is offset to the character's right, with a softly angled hanging edge.
    m=_mesher();grid=[]
    for j in range(nr+1):
        v=j/nr;row=[]
        for k in range(35):
            a=_lerp(-.69,.26+.045*math.sin(v*math.pi),k/34)
            co=at(a,v,.006)
            co.z-=.011*(1-v)**3*(k/34)
            row.append(_v(m,co))
        grid.append(row)
    for j in range(nr):
        for k in range(34):_f(m,[grid[j][k],grid[j+1][k],grid[j+1][k+1],grid[j][k+1]])
    flap=_emit(m,key+'_sarong_front_overlap','sarong',uv=(low,high))
    md=flap.modifiers.new('OverlapThickness','SOLIDIFY');md.thickness=.0028;md.offset=0
    bpy.context.view_layer.objects.active=flap;bpy.ops.object.modifier_apply(modifier=md.name)
    # The rolled fastening lies under this untucked shirt. Omit the concealed
    # roll/knot so it cannot penetrate the belly or protrude as green buttons.
    return [main,flap]


def _feet(s):
    key=s['variant'];fl=s['foot'];fw=.072 if key=='D' else (.097 if key=='C' else .085)
    skin=_mesher();sole=_mesher();straps=_mesher()
    for sign in (-1,1):
        x=sign*s['foot_x'];z=s['wrap_low']+.038
        _tube(skin,[(x,.010,.08),(x,.013,z+.04)],[(fw*.54,fw*.61),(fw*.67,fw*.69)],32)
        _ball(skin,(x,-fl*.18,.058),(fw*.87,fl*.47,.053),40,26)
        # A broad plantar pad seats the foot on the flat footbed. A rounded
        # ellipsoid alone arches upward around its rim and appears to float.
        before=set(skin.bm.verts)
        R.loft(skin,[(.034,fw*.79,fl*.48,-fl*.19),
                     (.039,fw*.85,fl*.49,-fl*.19),
                     (.055,fw*.82,fl*.47,-fl*.19),
                     (.070,fw*.66,fl*.38,-fl*.17)],'skin',{},seg=48,step=.004)
        for v in set(skin.bm.verts)-before:v.co.x+=x
        for i in range(5):
            # Big toe sits toward the inside, followed by graduated smaller toes.
            u=i/4;xx=x+sign*_lerp(-fw*.64,fw*.65,u)
            radius=_lerp(.019,.011,u);yy=-fl*(.615-.050*u)
            _ball(skin,(xx,yy,.048),(radius,.032*(1-.24*u),radius*.76),20,14)
        # Bevelled oval sole with a genuinely flat upper footbed, not a second
        # ellipsoid balancing beneath the skin. The skin overlaps it by 3 mm.
        before=set(sole.bm.verts)
        R.loft(sole,[(.000,fw*.94,fl*.53,-fl*.20),
                     (.005,fw,fl*.55,-fl*.20),
                     (.031,fw,fl*.55,-fl*.20),
                     (.037,fw*.95,fl*.525,-fl*.20)],'skin',{},seg=64,step=.004)
        for v in set(sole.bm.verts)-before:v.co.x+=x
        for yy,depth in [(-fl*.34,.025),(-fl*.035,.029)]:
            pts=[]
            for i in range(49):
                a=math.pi*i/48;pts.append((x-fw*.91*math.cos(a),yy,.031+.080*math.sin(a)**.72))
            _tube(straps,pts,[(.009,depth)]*49,16)
    return [_emit(skin,key+'_feet_ankles','skin',union=.0027,smooth=3),
            _emit(sole,key+'_sandal_soles','sole'),_emit(straps,key+'_sandal_straps','sandal')]


def build_variant(identifier):
    """Create one static sculpt study in a fresh scene; no exports or renders."""
    key=str(identifier).upper()
    if key not in SPECS:raise ValueError('Variant must be A, B, C or D')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    s=dict(SPECS[key],variant=key)
    head,front=_face_shape(s)
    objects=[head]+_features(s,front)+_hair_and_hat(s,front)+_body(s)+_sarong(s)+_feet(s)
    bpy.context.view_layer.update()
    for ob in objects:
        ob['variant']=key;ob['study_label']=s['label']
    vertices=[v.co for ob in objects for v in ob.data.vertices]
    lo=[min(v[i] for v in vertices) for i in range(3)]
    hi=[max(v[i] for v in vertices) for i in range(3)]
    metadata=dict(id=key,label=s['label'],height_m=hi[2]-lo[2],bounds_min=lo,bounds_max=hi,
        target_height_m=1.66,head_height_target_m=2*s['rz'],head_count_target=s['heads'],
        triangle_count=sum(sum(len(f.vertices)-2 for f in ob.data.polygons) for ob in objects),
        objects=len(objects),coordinates='metres; +Z up; -Y front; ground at z=0',
        purpose='Static art-direction sculpt comparison. Not a game-ready rig.',
        references={'A':'Warm-animation reinterpretation of identity sheet',
                    'B':'round-03/references/B.png; graphic facial planes',
                    'C':'round-03/references/C.png; compact sleepy miniature',
                    'D':'round-03/references/A.png; grounded adult reference'},
        inferred=['Exact head/body ratios are authored direction targets, not traced reference measurements',
                  'Back anatomy, fingers and feet, cloth interior and fold placement',
                  'Side/profile facial depth and all natural anatomical transitions'],
        parameters=s)
    return dict(objects=objects,roles={ob.name:ob['role'] for ob in objects},metadata=metadata)


if __name__=='__main__':
    import json
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['A']
    result=build_variant(args[0])
    print('LOCAL_GEOMETRY',json.dumps(result['metadata']),flush=True)
