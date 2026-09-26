"""Refined KL cast: continuous sampled forms, fitted faces and real garment shells.

Metres, Z up, -Y front. Family dimensions derive from approved round-01 sheets and
Tools/refined_characters reference measurements. Hidden depths and NPC variations
are inferred. Existing Humanoid names / palette cells are deliberately retained.
"""
import math
import random
import bmesh
import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree
import kl_core as K
import kl_humanoid as H
import kl_characters as C

TAU = math.tau
B = lambda **kw: kw

# Display colours sampled from the approved family direction. Separate character
# atlas; never rewrite the environment / vehicle palette.
COLORS = dict(skin=(.79,.49,.28), skin_shadow=(.68,.36,.20), hair=(.055,.047,.047),
    brow=(.065,.044,.035), pupil=(.035,.03,.029), eye_white=(.98,.965,.92),
    mouth=(.28,.075,.06), teeth=(.99,.97,.91), batik_orange=(.70,.28,.115),
    batik_dark=(.38,.12,.115), gold=(.77,.51,.19), sarong_green=(.13,.24,.18),
    sarong_check=(.28,.36,.255), kurung_pink=(.85,.39,.39), kurung_skirt=(.80,.35,.35),
    kurung_flower=(.55,.19,.23), tudung_blue=(.31,.45,.64), slipper_blue=(.12,.23,.36),
    tshirt_red=(.67,.18,.16), shorts_blue=(.13,.20,.31), sneaker=(.13,.22,.32),
    sole=(.88,.845,.75), dress_red=(.92,.32,.31), dress_trim=(.96,.93,.86),
    cord_red=(.75,.12,.12), songkok=(.045,.04,.043), sandal_brown=(.24,.15,.105),
    umb_yellow=(.91,.64,.15), pastel_lilac=(.59,.43,.67), pastel_mint=(.40,.64,.56),
    sarong_red=(.48,.16,.19), sarong_red_check=(.70,.34,.31), batik_blue=(.16,.36,.53),
    batik_blue_dark=(.08,.20,.31), police_blue=(.10,.16,.28), suit_black=(.085,.09,.12))

def mix(a, b, t): return a + (b-a)*t
def smooth(t):
    t=max(0.,min(1.,t)); return t*t*(3-2*t)

def sample(rows, z):
    """Cubic Hermite interpolation removes bands from measured section tables."""
    if z <= rows[0][0]: return tuple(rows[0][1:])
    if z >= rows[-1][0]: return tuple(rows[-1][1:])
    for i in range(len(rows)-1):
        a,b=rows[i:i+2]
        if a[0] <= z <= b[0]:
            prev=rows[max(0,i-1)]; nxt=rows[min(len(rows)-1,i+2)]
            dt=b[0]-a[0]; t=(z-a[0])/dt
            out=[]
            for j in range(1,len(a)):
                ma=(b[j]-prev[j])/max(1e-8,b[0]-prev[0])
                mb=(nxt[j]-a[j])/max(1e-8,nxt[0]-a[0])
                val=(2*t**3-3*t*t+1)*a[j]+(t**3-2*t*t+t)*dt*ma+(-2*t**3+3*t*t)*b[j]+(t**3-t*t)*dt*mb
                out.append(val)
            return tuple(out)

def vertex(m, co, weights, tag=None):
    v=m.bm.verts.new(co); m.weights.append((v,weights))
    if tag: m.tags.setdefault(tag,[]).append(v)
    return v

def face(m, verts, color):
    try: f=m.bm.faces.new(verts)
    except ValueError: return
    f.smooth=True
    for lp in f.loops: lp[m.uv].uv=K.pal_uv(color)

def tube(m, points, radii, color, weights, tag=None, sides=12, ref=(0,1,0), caps=True):
    """Ring-based curve with explicit deform weights and smooth rounded end caps."""
    pts=[Vector(p) for p in points]; rings=[]
    for i,p in enumerate(pts):
        tangent=(pts[min(i+1,len(pts)-1)]-pts[max(i-1,0)]).normalized()
        side=Vector(ref).cross(tangent).normalized()
        if side.length < .5: side=Vector((1,0,0)).cross(tangent).normalized()
        dep=tangent.cross(side).normalized()
        rr=radii[i] if isinstance(radii,list) else radii
        ra,rb=rr if isinstance(rr,(tuple,list)) else (rr,rr)
        wg=weights(i/(len(pts)-1),p) if callable(weights) else weights
        rings.append([vertex(m,p+side*math.cos(TAU*k/sides)*ra+dep*math.sin(TAU*k/sides)*rb,wg,tag) for k in range(sides)])
    for i in range(len(rings)-1):
        col=color(i/(len(rings)-1)) if callable(color) else color
        for j in range(sides): face(m,[rings[i][j],rings[i][(j+1)%sides],rings[i+1][(j+1)%sides],rings[i+1][j]],col)
    if caps:
        for i in (0,len(pts)-1):
            wg=weights(i/(len(pts)-1),pts[i]) if callable(weights) else weights
            v=vertex(m,pts[i],wg,tag)
            col=color(i/(len(pts)-1)) if callable(color) else color
            for j in range(sides): face(m,[v,rings[i][j],rings[i][(j+1)%sides]],col)

def bezier(points, n=16):
    ps=[Vector(p) for p in points]
    def at(t):
        q=ps[:]
        while len(q)>1: q=[a.lerp(b,t) for a,b in zip(q,q[1:])]
        return q[0]
    return [at(i/(n-1)) for i in range(n)]

def stroke(m, points, radius, color, weights, tag=None, n=16):
    pts=bezier(points,n)
    rs=[radius*(.35+.65*math.sin(math.pi*i/(n-1))**.35) for i in range(n)]
    tube(m,pts,rs,color,weights,tag,sides=8)

def torso_weights(p,z):
    hz,sz=p['hip_z'],p['shoulder_z']
    return H._chain(z,[(hz+.07,'Hips','Spine',.055),(hz+(sz-hz)*.51,'Spine','Chest',.07)])

def garment_weights(p,co):
    """Shared by the garment and its printed details, including moving hems."""
    side='Left' if co.x>0 else 'Right';sx=p['shoulder_x'];al=p['arm_len']
    torso=torso_weights(p,co.z)
    arms=H._chain(abs(co.x),[(sx+.005,'Chest',side+'UpperArm',.035),
        (sx+al*.45,side+'UpperArm',side+'LowerArm',.055),
        (sx+al*.86-.007,side+'LowerArm',side+'Hand',.018)])
    outer=smooth((abs(co.x)-(sx-.015))/.070)
    zband=smooth((co.z-(p['shoulder_z']-.20))/.065)
    # The completed transition is below the entire sleeve cross-section.
    # Keep the z gate even on broad torsos, whose waist can extend past the arm socket.
    af=outer*zband
    ws={k:torso.get(k,0)*(1-af)+arms.get(k,0)*af for k in set(torso)|set(arms)}
    if p.get('draped_lower') and co.z<p['hip_z']+.035:
        blend=.85*smooth((p['hip_z']+.035-co.z)/.13)
        left=smooth(.5+co.x/(p['garment_width']*.40))
        ws={'Hips':1-blend,'LeftUpperLeg':blend*left,'RightUpperLeg':blend*(1-left)}
    ws=dict(sorted(ws.items(),key=lambda kv:-kv[1])[:4]);total=sum(ws.values())
    return {k:v/total for k,v in ws.items() if v>1e-8}

def loft(m, rows, color, weights, tag=None, seg=48, step=.014, folds=0., close=True):
    n=max(8,int((rows[-1][0]-rows[0][0])/step)); rings=[]
    for i in range(n+1):
        z=mix(rows[0][0],rows[-1][0],i/n); rx,ry,cy=sample(rows,z)
        ring=[]
        for j in range(seg):
            a=j/seg*TAU
            wr=1+folds*math.cos(10*a)*(.3+.7*(1-i/n))
            co=Vector((rx*math.sin(a)*wr,cy-ry*math.cos(a)*wr,z))
            wg=weights(co) if callable(weights) else weights
            ring.append(vertex(m,co,wg,tag))
        rings.append(ring)
    for i in range(n):
        for j in range(seg):
            a=(j+.5)/seg*TAU; z=mix(rows[0][0],rows[-1][0],(i+.5)/n)
            col=color(a,z) if callable(color) else color
            face(m,[rings[i][j],rings[i][(j+1)%seg],rings[i+1][(j+1)%seg],rings[i+1][j]],col)
    if close:
        for i in (0,n):
            z=rows[0 if i==0 else -1][0]; cy=rows[0 if i==0 else -1][3]
            wg=weights(Vector((0,cy,z))) if callable(weights) else weights
            v=vertex(m,(0,cy,z),wg,tag)
            col=color(0,z) if callable(color) else color
            for j in range(seg): face(m,[v,rings[i][j],rings[i][(j+1)%seg]],col)
    return rows

def shell_point(rows,x,z,side=-1,lift=.002):
    rx,ry,cy=sample(rows,z); u=max(-.999,min(.999,x/rx))
    return Vector((x,cy+side*(ry*math.sqrt(1-u*u)+lift),z))

def decal(m,rows,outline,color,p,side=-1):
    # Conforming polygon patch: tessellated in x/z, never a box stuck to the shirt.
    cx=sum(v[0] for v in outline)/len(outline); cz=sum(v[1] for v in outline)/len(outline)
    tag='decal_front' if side<0 else 'decal_back'
    center=shell_point(rows,cx,cz,side,.004)
    cv=vertex(m,center,garment_weights(p,center),tag)
    rings=[]
    for i in range(1,5):
        t=i/4
        coords=[shell_point(rows,mix(cx,x,t),mix(cz,z,t),side,.004) for x,z in outline]
        rings.append([vertex(m,co,garment_weights(p,co),tag) for co in coords])
    for j in range(len(outline)):face(m,[cv,rings[0][j],rings[0][(j+1)%len(outline)]],color)
    for i in range(3):
        for j in range(len(outline)):face(m,[rings[i][j],rings[i][(j+1)%len(outline)],rings[i+1][(j+1)%len(outline)],rings[i+1][j]],color)

def flower(m,rows,x,z,r,color,p,side):
    # Single connected five-petal print, flat on garment.
    outline=[]
    for k in range(50):
        a=TAU*k/50; rr=r*(.72+.28*math.cos(5*a))
        outline.append((x+rr*math.cos(a),z+rr*math.sin(a)))
    decal(m,rows,outline,color,p,side)
    decal(m,rows,[(x+r*.16*math.cos(TAU*k/12),z+r*.16*math.sin(TAU*k/12)) for k in range(12)],'gold',p,side)

def leaf(m,rows,x,z,r,angle,color,p,side):
    outline=[]
    for k in range(20):
        t=TAU*k/20; u=r*math.cos(t); v=r*.36*math.sin(t)*abs(math.sin(t))**.3
        outline.append((x+u*math.cos(angle)-v*math.sin(angle),z+u*math.sin(angle)+v*math.cos(angle)))
    decal(m,rows,outline,color,p,side)

def patterns(m,rows,p,kind,color):
    low,high=rows[0][0]+.08,rows[-1][0]-.10
    for side in (-1,1):
        for j in range(4):
            z=mix(low,high,j/3); rx=sample(rows,z)[0]
            for k in range(3):
                x=(k-1)*rx*.65+(j%2-.5)*.025
                if kind=='flower': flower(m,rows,x,z,.039 if p['height']>1.3 else .027,color,p,side)
                else:
                    for l in range(3): leaf(m,rows,x+(l-1)*.018,z+(l-1)*.022,.034,(l-1)*.8+.8,color if l<2 else 'gold',p,side)

# Family proportions measured from front view; physical sizes are approved sheet
# target heights. Head and limb depths use side view; NPC form adaptations inferred.
SPECS={
 'chr_pakmat':dict(height=1.66,headc=1.34,headrx=.185,headry=.157,headrz=.212,chin=1.145,shoulder=.239,sz=1.12,hip=.65,hx=.235,knee=.36,ankle=.08,arm=.59,bodyw=.312,bodyd=.30,hem=.61,foot=.34,shirt='batik_orange',pants='sarong_green',hair='songkok',skirt=True,hat=.135,nose=1.10,moustache=True,pattern='batik'),
 'chr_maksom':dict(height=1.57,headc=1.345,headrx=.168,headry=.15,headrz=.18,chin=1.16,shoulder=.225,sz=1.075,hip=.61,hx=.18,knee=.32,ankle=.075,arm=.50,bodyw=.287,bodyd=.235,hem=.405,foot=.29,shirt='kurung_pink',pants='kurung_skirt',hair='tudung',skirt=True,scarf='tudung_blue',long=True,nose=.72,pattern='flower'),
 'chr_along':dict(height=1.64,headc=1.355,headrx=.145,headry=.137,headrz=.175,chin=1.20,shoulder=.147,sz=1.087,hip=.625,hx=.134,knee=.345,ankle=.145,arm=.485,bodyw=.178,bodyd=.118,hem=.66,foot=.305,shirt='tshirt_red',pants='shorts_blue',hair='curly',shorts=True,nose=.95,sneakers=True,band=True),
 'chr_adik':dict(height=1.12,headc=.85,headrx=.215,headry=.173,headrz=.213,chin=.685,shoulder=.170,sz=.627,hip=.325,hx=.103,knee=.175,ankle=.06,arm=.305,bodyw=.200,bodyd=.175,hem=.215,foot=.215,shirt='dress_red',pants='dress_red',hair='pigtails',dress=True,nose=.50,child=True),
}

def npc(base,**kw):
    s=SPECS[base].copy();s.update(kw);return s

SPECS.update({
 'chr_aiman':npc('chr_along',height=1.72,headc=1.435,sz=1.167,chin=1.28,hip=.68,knee=.38,hem=.68,hair='swept',band=False,shirt='teal',pants='charcoal',shorts=False,satchel=True),
 'chr_mei':npc('chr_maksom',height=1.60,headc=1.37,chin=1.20,sz=1.10,headrx=.158,headrz=.187,bodyw=.245,hem=.71,hair='bun',shirt='coral',pants='charcoal',pattern=None,skirt=False,long=False,sneakers=False,glasses=True),
 'chr_ravi':npc('chr_pakmat',height=1.76,headc=1.475,chin=1.27,sz=1.18,headrx=.179,headrz=.207,bodyw=.282,bodyd=.19,hip=.78,knee=.42,hem=.70,shirt='ochre',pants='charcoal',hair='sidepart',hat=0.,skirt=False,sneakers=True,moustache=True,pattern='check'),
 'chr_townman':npc('chr_along',height=1.70,headc=1.405,chin=1.245,sz=1.14,hip=.74,knee=.40,hem=.68,shoulder=.188,bodyw=.215,shirt='batik_blue',pants='pants_khaki',shorts=False,pattern='batik',band=False,hair_density=.40),
 'chr_townaunty':npc('chr_maksom',height=1.56,headc=1.28,chin=1.12,sz=1.065,bodyw=.31,headrx=.183,shirt='pastel_lilac',pants='sarong_red',scarf='pastel_mint'),
 'chr_pakcik':npc('chr_pakmat',height=1.63,headc=1.31,chin=1.115,sz=1.09,bodyw=.252,bodyd=.175,shirt='baju_white',pants='baju_white',skirt=False,pattern=None,long=True,grey=True,glasses=True),
 'chr_kid':npc('chr_adik',height=1.15,headc=.885,chin=.72,sz=.665,hip=.38,hem=.36,shoulder=.145,bodyw=.169,headrx=.19,headrz=.194,hair='swept',dress=False,shirt='kid_shirt',pants='kid_shorts',shorts=True,sneakers=True),
 'chr_polis':npc('chr_ravi' if 'chr_ravi' in SPECS else 'chr_pakmat',height=1.76,headc=1.465,chin=1.27,sz=1.22,hip=.78,knee=.41,hem=.70,headrx=.18,headrz=.20,bodyw=.268,bodyd=.19,shirt='police_blue',pants='police_blue',hair='police',skirt=False,pattern=None,long=True,sneakers=True,police=True),
 'chr_datukmega':npc('chr_pakmat',height=1.74,headc=1.405,chin=1.21,sz=1.15,bodyw=.365,bodyd=.265,shoulder=.28,hip=.66,knee=.34,hem=.58,headrx=.20,headry=.173,shirt='suit_black',pants='suit_black',skirt=False,pattern=None,long=True,sneakers=True,shades=True,suit=True),
})

def proportions(s):
    neck=max(s['sz']+.062,s['chin']-.025)
    return dict(height=s['height'],top_z=s['height'],head_z=neck+.025,head=2*s['headrz'],
        neck_z=neck,shoulder_z=s['sz'],hip_z=s['hip'],knee_z=s['knee'],ankle_z=s['ankle'],
        hip_x=s['hx'],shoulder_x=s['shoulder'],arm_len=s['arm'],foot_len=s['foot'],
        draped_lower=bool(s.get('dress') or s['hair']=='tudung'),garment_width=s['bodyw'])

def make_head(m,p,s):
    cx,rx,ry,rz=s['headc'],s['headrx'],s['headry'],s['headrz']
    # A single closed sculptural surface. Cheeks, jaw and brow volumes are blended
    # in its coordinates instead of overlapping skull/muzzle spheres.
    def surface(a,t):
        z=cx+rz*t; rr=math.sqrt(max(0.,1-t*t))
        jaw=1+.075*math.exp(-((t+.40)/.29)**2)-.055*max(0,-t)
        x=rx*rr*math.sin(a)*jaw
        y=-ry*rr*math.cos(a)
        front=max(0,math.cos(a))**6
        y-=front*(.022*math.exp(-((t+.42)/.24)**2)+.006*math.exp(-((t-.40)/.35)**2))
        return Vector((x,y,z))
    seg=64; n=42; rings=[]
    for i in range(1,n):
        t=-math.cos(math.pi*i/n)
        rings.append([vertex(m,surface(TAU*j/seg,t),B(Head=1),'head_surface') for j in range(seg)])
    for i in range(len(rings)-1):
        for j in range(seg):face(m,[rings[i][j],rings[i][(j+1)%seg],rings[i+1][(j+1)%seg],rings[i+1][j]],'skin')
    for idx,z in ((0,cx-rz),(-1,cx+rz)):
        v=vertex(m,(0,0,z),B(Head=1),'head_surface')
        for j in range(seg): face(m,[v,rings[idx][j],rings[idx][(j+1)%seg]],'skin')
    def front(x,z,lift=0):
        t=max(-.99,min(.99,(z-cx)/rz));rr=math.sqrt(1-t*t)
        a=math.asin(max(-.99,min(.99,x/(rx*rr))))
        pt=surface(a,t);pt.x=x;pt.y-=lift;return pt
    # Neck overlaps internally by a few mm and deforms with the neck bone.
    tube(m,[(0,0,s['sz']-.02),(0,0,s['chin']+.08)],[(rx*.34,ry*.43)]*2,'skin',B(Neck=1),sides=24)
    def eye_patch(center_x,center_z,ax,az,color,tag,lift):
        rings=[]; sides=40
        for k in range(1,10):
            r=k/9; ring=[]
            for j in range(sides):
                a=TAU*j/sides
                x=center_x+ax*r*math.cos(a);z=center_z+az*r*math.sin(a)
                ring.append(vertex(m,front(x,z,lift+.005*(1-r*r)),B(Head=1),tag))
            rings.append(ring)
        cv=vertex(m,front(center_x,center_z,lift+.005),B(Head=1),tag)
        for j in range(sides):face(m,[cv,rings[0][j],rings[0][(j+1)%sides]],color)
        for k in range(len(rings)-1):
            for j in range(sides):face(m,[rings[k][j],rings[k][(j+1)%sides],rings[k+1][(j+1)%sides],rings[k+1][j]],color)
    ex=rx*(.34 if s.get('moustache') else .40)
    ez=cx+rz*.28
    erx=rx*(.225 if s.get('moustache') else (.25 if not s.get('child') else .29))
    erz=rz*(.255 if s.get('moustache') else .29)
    for sign,side in ((1,'L'),(-1,'R')):
        ec=front(sign*ex,ez,.006)
        # Eye whites follow the cheek plane with slight corneal volume. This
        # removes the bolted-on eyeball silhouette in profile and 3/4 views.
        eye_patch(sign*ex,ez,erx,erz,'eye_white','eye_'+side,.004)
        eye_patch(sign*ex-sign*erx*.04,ez-erz*.01,erx*.35,erz*.43,'pupil','eye_'+side,.011)
        eye_patch(sign*ex-erx*.10,ez+erz*.17,erx*.085,erz*.085,'eye_white','eye_'+side,.018)
        pts=[]
        for k in range(21):
            a=math.pi*k/20
            pts.append(front(ec.x+erx*1.015*math.cos(a),ec.z+erz*1.015*math.sin(a),.003))
        tube(m,pts,[.0025]*len(pts),'skin_shadow',B(Head=1),'eye_'+side,sides=6)
        bx=sign*ex; bz=ez+erz+.022
        stroke(m,[front(bx-sign*erx*.94,bz-.009,.015),front(bx,bz+.032,.015),front(bx+sign*erx,bz-.001,.012)],.0095,'brow',B(Head=1),'brow_'+side,n=18)
        if s.get('hair')!='tudung':
            ear=(sign*rx*.98,-.002,cx-rz*.03)
            m.ball(ear,(rx*.22,ry*.19,rz*.33),'skin',seg=24,rings=18,bones=B(Head=1))
            m.ball((ear[0]+sign*rx*.035,-ry*.165,ear[2]),(rx*.115,.010,rz*.19),'skin_shadow',seg=18,rings=12,bones=B(Head=1))
    nose=s.get('nose',1); np=front(0,cx-rz*.035,.015)
    m.ball(np+Vector((0,-.014,0)),(rx*.215*nose,.042*nose,rz*.15*nose),'skin',seg=32,rings=22,bones=B(Head=1))
    # A thin smile seated into the continuous cheek plane.
    mz=cx-rz*.48; mw=rx*.50
    # Mouth is a conforming dark crescent with a small upper tooth plane, all
    # following the face surface; no protruding muzzle or rectangular jaw.
    # Subdivide along both axes so every face lies in front of the curved cheek
    # surface. A single fan would sink into the head and leave only floating teeth.
    mouth_grid=[]
    for j in range(6):
        v=j/5;row=[]
        for k in range(31):
            u=-.998+1.996*k/30
            top=mz+.014-.010*(1-u*u);bottom=mz+.014-.040*(1-u*u)
            row.append(vertex(m,front(u*mw,mix(top,bottom,v),.005),B(Head=1),'mouth'))
        mouth_grid.append(row)
    for j in range(5):
        for k in range(30):face(m,[mouth_grid[j][k],mouth_grid[j+1][k],mouth_grid[j+1][k+1],mouth_grid[j][k+1]],'mouth')
    for k in range(20):
        u0=-.87+1.74*k/20;u1=-.87+1.74*(k+1)/20
        coords=[(u0*mw,mz+.010-.008*(1-u0*u0)),(u1*mw,mz+.010-.008*(1-u1*u1)),(u1*mw,mz-.003-.008*(1-u1*u1)),(u0*mw,mz-.003-.008*(1-u0*u0))]
        face(m,[vertex(m,front(x,z,.010),B(Head=1),'teeth') for x,z in coords],'teeth')
    if s.get('moustache'):
        for sign in (-1,1):
            stroke(m,[front(0,cx-rz*.22,.023),front(sign*rx*.27,cx-rz*.18,.024),front(sign*rx*.5,cx-rz*.36,.018)],.028 if not s.get('grey') else .022,'grey_hair' if s.get('grey') else 'hair',B(Head=1),'moustache',n=20)
    if s.get('glasses') or s.get('shades'):
        for sign in (-1,1):
            center=front(sign*ex,ez,.038)
            if s.get('shades'):
                m.ball(center,(erx*1.17,.013,erz*.83),'pupil',seg=24,rings=14,bones=B(Head=1))
            else:
                pts=[center+Vector((erx*1.21*math.cos(TAU*k/36),0,erz*1.12*math.sin(TAU*k/36))) for k in range(37)]
                tube(m,pts,[.004]*37,'metal_dark',B(Head=1),sides=8,caps=False)
        stroke(m,[front(-ex*.3,ez,.041),front(0,ez+.01,.055),front(ex*.3,ez,.041)],.004,'metal_dark',B(Head=1))
    return front

def make_hair(m,p,s):
    rx,ry,rz,hz=s['headrx'],s['headry'],s['headrz'],s['headc']
    style=s['hair']; color='grey_hair' if s.get('grey') else 'hair'
    if style=='tudung':
        make_tudung(m,p,s);return
    # Partial scalp surface follows head; front hairline opens above eyebrows.
    seg=48;nr=18;rings=[]
    for i in range(nr+1):
        row=[]
        for k in range(seg):
            a=TAU*k/seg;front=max(0,math.cos(a))
            phi_end=mix(2.05,.96,front**3)
            phi=.025+(phi_end-.025)*i/nr
            co=(rx*1.035*math.sin(phi)*math.sin(a),-ry*1.035*math.sin(phi)*math.cos(a)+.008,hz+rz*1.025*math.cos(phi))
            row.append(vertex(m,co,B(Head=1)))
        rings.append(row)
    for i in range(nr):
        for j in range(seg):face(m,[rings[i][j],rings[i][(j+1)%seg],rings[i+1][(j+1)%seg],rings[i+1][j]],color)
    if style in ('songkok','police'):
        low=s['height']-(.135 if style=='songkok' else .12)
        rows=[(low,rx*.84,ry*.88,.006),(low+.015,rx*.90,ry*.91,.006),
              (s['height']-.014,rx*.76,ry*.79,.012),(s['height'],rx*.72,ry*.76,.012)]
        loft(m,rows,'songkok' if style=='songkok' else 'police_blue',B(Head=1),step=.01,seg=48)
        if style=='police':
            m.ball((0,-ry*.85,low+.018),(rx*.82,ry*.70,.017),'suit_black',seg=28,rings=10,bones=B(Head=1))
            m.ball((0,-ry*.94,low+.074),(.028,.006,.035),'gold',seg=16,rings=12,bones=B(Head=1))
    elif style=='curly':
        # Deliberately designed large locks, not random spikes or dozens of beads.
        crown=s['height']-.07; m.ball((0,.018,crown-.08),(rx*1.12,ry*1.08,.135),color,seg=32,rings=22,bones=B(Head=1))
        for j,(z,rad,n) in enumerate(((crown-.12,1.,11),(crown-.025,.78,8),(crown+.015,.35,4))):
            for k in range(n):
                a=TAU*(k+.25*(j%2))/n
                co=(rx*rad*math.sin(a),.02+ry*rad*math.cos(a),z+.015*math.sin(k*2.1))
                m.ball(co,(.052,.054,.063),color,seg=18,rings=12,bones=B(Head=1))
        for k in range(5):
            x=(k-2)*rx*.34
            m.ball((x,-ry*.81,hz+rz*(.68+.10*math.sin(k))),(.047,.038,.053),color,seg=20,rings=14,bones=B(Head=1))
    elif style=='pigtails':
        # Swept fringe is broad continuous tapered locks; bobbles and tails separate.
        for sign in (-1,1):
            stroke(m,[(sign*.015,-.045,hz+rz*.95),(sign*rx*.58,-ry*.77,hz+rz*.72),(sign*rx*.80,-ry*.70,hz+rz*.24)],.044,color,B(Head=1),n=20)
            knot=Vector((sign*rx*1.05,.018,hz+rz*.44))
            m.ball(knot,(.037,.035,.041),'cord_red',seg=22,rings=14,bones=B(Head=1))
            pts=bezier([knot+Vector((sign*.01,.004,-.012)),knot+Vector((sign*.12,.015,-.035)),knot+Vector((sign*.095,.017,-.155))],22)
            rs=[.05*math.sin(math.pi*(i/(len(pts)-1)*.82+.10))**.65 for i in range(len(pts))]
            rs[-1]=.002
            tube(m,pts,rs,color,B(Head=1),sides=18)
    else:
        # Swept adult locks and compact child's quiff.
        for k in range(5):
            x=(k-2)*rx*.29
            stroke(m,[(x,-ry*.82,hz+rz*.48),(x+rx*.22,-ry*.73,s['height']-.005),(x+rx*.47,ry*.2,hz+rz*.68)],rx*.26,color,B(Head=1),n=22)
        if style=='bun': m.ball((0,ry*.99,hz+rz*.42),(rx*.55,ry*.52,rz*.56),color,seg=24,rings=18,bones=B(Head=1))

def make_tudung(m,p,s):
    rx,ry,rz,hz=s['headrx'],s['headry'],s['headrz'],s['headc'];col=s['scarf']
    # Open annular hood: the face opening and crown/back are one continuous shell.
    seg=64;nr=28;rings=[]
    for i in range(nr+1):
        t=i/nr; row=[]
        for j in range(seg):
            a=TAU*j/seg
            # Face opening in XZ; sweep around crown/back to close behind head.
            x=rx*.99*math.cos(a); z=hz+rz*1.02*math.sin(a)-.009
            y=-ry*math.sqrt(max(.03,1-(x/(rx*1.10))**2))-.007
            angle=t*math.pi*.93
            xx=x*(1+.15*math.sin(angle))*math.cos(angle*.52)
            zz=hz+(z-hz)*(1+.12*math.sin(angle))
            yy=mix(y,ry*1.08,t)+.026*math.sin(angle)
            row.append(vertex(m,(xx,yy,zz),B(Head=1)))
        rings.append(row)
    for i in range(nr):
        for j in range(seg):face(m,[rings[i][j],rings[i][(j+1)%seg],rings[i+1][(j+1)%seg],rings[i+1][j]],col)
    # Draped shoulder scarf, continuously connected ring with pointed lower edge.
    rings=[]
    for i in range(19):
        t=i/18; row=[]
        for j in range(seg):
            a=TAU*j/seg; front=math.cos(a)
            topz=s['chin']+.04
            bottomz=s['sz']-.16-.09*abs(front)
            rad=mix(rx*.55,s['bodyw']*.91,t)
            dep=mix(ry*.80,s['bodyd']*1.18,t)
            z=mix(topz,bottomz,t)+.009*math.sin(3*a)*math.sin(math.pi*t)
            co=(rad*math.sin(a),-dep*math.cos(a)-.013,z)
            wg={'Head':1-t,'Chest':t} if t<.65 else B(Chest=1)
            row.append(vertex(m,co,wg))
        rings.append(row)
    for i in range(18):
        for j in range(seg):face(m,[rings[i][j],rings[i][(j+1)%seg],rings[i+1][(j+1)%seg],rings[i+1][j]],col)

def make_body(m,p,s):
    w,d,sz,hem=s['bodyw'],s['bodyd'],s['sz'],s['hem'];hip=s['hip']
    top=max(s['chin']-.035,sz+.07)
    low=hem if not s.get('dress') else s['hem']
    broad=s['bodyw']>.30
    rows=[(low,w*(1.08 if s.get('dress') else 1),d,.0),
          (low+.025,w*(1.065 if s.get('dress') else 1),d*1.01,-.005),
          (low+.10,w,d*1.035,-.015),
          (max(low+.13,sz-.22),w*.965,d*(.81 if broad else .98),-.01),
          (sz-.065,w*.88,d*(.68 if broad else .80),.0),(sz+.005,s['shoulder']*.93,d*.60,0),
          (top,.072 if not s.get('child') else .06,.065,0)]
    rows=sorted(rows,key=lambda r:r[0])
    # Avoid inverted neck sections on especially broad/short child torsos.
    for i in range(1,len(rows)):
        if rows[i][0]<=rows[i-1][0]:rows[i]=(rows[i-1][0]+.005,*rows[i][1:])
    if s.get('dress'):
        col=lambda a,z:'dress_trim' if z<low+.027 else s['shirt']
    else:col=s['shirt']
    loft(m,rows,col,lambda co:torso_weights(p,co.z),'torso',seg=56,folds=.012)
    if s.get('pattern') in ('batik','flower'):
        patterns(m,rows,p,s['pattern'],'batik_dark' if s['pattern']=='batik' else 'kurung_flower')
    if s.get('pattern')=='check':
        # Printed checks use face colours rather than raised bars.
        for f in m.bm.faces:
            if any(v in set(m.tags.get('torso',[])) for v in f.verts):
                co=f.calc_center_median()
                if (co.z*18)%1 <.10 or (co.x*18)%1<.10:
                    for lp in f.loops:lp[m.uv].uv=K.pal_uv('ochre_check')
    # Collar follows the neckline, with real curved fold instead of square blocks.
    if s['hair']!='tudung':
        if s.get('dress'):
            for sign in (-1,1):
                outline=[(sign*.006,top-.006),(sign*.07,top+.004),(sign*.097,top-.035),(sign*.055,top-.059),(sign*.009,top-.035)]
                decal(m,rows,outline,'dress_trim',p)
        elif s.get('pattern') or s.get('suit') or s.get('police') or s['hair']=='songkok':
            for sign in (-1,1):
                outline=[(sign*.022,top-.008),(sign*.092,top-.035),(sign*.111,top-.092),(sign*.036,top-.063)]
                collar_color='batik_dark' if s['shirt']=='batik_orange' else ('navy' if s.get('police') else s['shirt'])
                decal(m,rows,outline,collar_color,p)
                pts=[shell_point(rows,x,z,lift=.005) for x,z in outline+[outline[0]]]
                tube(m,pts,[.0035]*len(pts),s['shirt'],B(Chest=1),sides=6)
            if not s.get('suit'):
                pts=[shell_point(rows,0,mix(hem+.016,sz-.046,i/28),lift=.006) for i in range(29)]
                tube(m,pts,[.0025]*29,'batik_dark' if s['shirt']=='batik_orange' else s['shirt'],lambda t,co:torso_weights(p,co.z),sides=6)
            for j in range(4):
                z=mix(hem+.065,sz-.06,j/3);co=shell_point(rows,0,z,lift=.005)
                m.ball(co,(.009,.006,.009),'batik_dark' if s['shirt']=='batik_orange' else 'metal_dark',seg=12,rings=8,bones=torso_weights(p,z))
        else:
            pts=[(.066*math.sin(TAU*k/48),-.06*math.cos(TAU*k/48),top+.002) for k in range(49)]
            tube(m,pts,[.008]*49,'dress_trim',B(Chest=1),sides=8,caps=False)
    if s.get('suit'):
        decal(m,rows,[(-.048,top),(.048,top),(.034,sz-.16),(-.035,sz-.16)],'white',p)
        decal(m,rows,[(-.014,top-.018),(.017,top-.018),(.029,sz-.25),(0,sz-.30),(-.029,sz-.25)],'tie_red',p)
    if s.get('police'):
        for sign in (-1,1):
            x=sign*w*.44;z=sz-.145
            decal(m,rows,[(x-.045,z+.031),(x+.045,z+.031),(x+.04,z-.038),(x-.04,z-.038)],'navy',p)
        co=shell_point(rows,-w*.44,sz-.133,lift=.006)
        m.ball(co,(.024,.005,.030),'gold',seg=16,rings=10,bones=B(Chest=1))
    if s.get('satchel'):
        for side in (-1,1):
            pts=[shell_point(rows,mix(-.09,.18,t),mix(sz+.006,hem+.03,t),side,.008) for t in [i/25 for i in range(26)]]
            tube(m,pts,[(.022,.009)]*26,'strap',lambda t,co:torso_weights(p,co.z),sides=10)
        m.ball((p['hip_x']+.13,0,hip-.11),(.083,.147,.135),'olive',seg=26,rings=18,bones=B(Satchel=1))
        m.ball((p['hip_x']+.175,-.005,hip-.06),(.055,.145,.084),'olive_dark',seg=24,rings=14,bones=B(Satchel=1))
        stroke(m,[(-p['hip_x']-.04,-.135,hip),(-p['hip_x']-.04,-.15,hip-.07),(-p['hip_x']-.04,-.14,hip-.15)],.006,'cord_red',B(KeyCord=1))
    return rows

def make_limbs(m,p,s):
    sz,sx,al=s['sz'],s['shoulder'],s['arm'];child=s.get('child',False)
    ar=.034 if child else (.055 if s.get('bodyw',.2)>.26 else .041)
    for sign,side in ((1,'Left'),(-1,'Right')):
        # Samples at elbow + on both sides keep the bend round.
        up,lo,hand=(side+k for k in ('UpperArm','LowerArm','Hand'))
        x0=sx-.017;wrist=sx+al*.86;elbow=sx+al*.45
        ts=[i/32 for i in range(33)];pts=[];rs=[]
        for t in ts:
            x=mix(x0,wrist,t);r=ar*(1.10-.30*t+.035*math.cos(TAU*t))
            pts.append((sign*x,0,sz));rs.append((r,r*.91))
        wg=lambda t,co,u=up,l=lo,h=hand:H._chain(abs(co.x),[(sx+.005,'Chest',u,.035),(elbow,u,l,.055),(wrist-.007,l,h,.018)])
        arm_before=set(m.bm.verts)
        tube(m,pts,rs,'skin',wg,sides=20)
        # Actual sleeve shell overlies the flesh with a rounded hem.
        end=wrist-.013 if s.get('long') else sx+al*.285
        # Covered anatomy remains in HIGH for editing. The game mesh omits it
        # so sleeve/skin bend differences cannot show flesh through shoulder cloth.
        m.tags.setdefault('concealed_anatomy',[]).extend(
            v for v in set(m.bm.verts)-arm_before if abs(v.co.x)<end-.013)
        pts=[(sign*mix(sx-.075,end,t),0,sz) for t in [i/18 for i in range(19)]]
        r0=ar*1.57;r1=ar*1.23 if s.get('long') else ar*1.51
        rs=[(mix(r0,r1,i/18)+.002*math.sin(math.pi*i/18),mix(r0,r1,i/18)*.96) for i in range(19)]
        tube(m,pts,rs,s['shirt'],wg,tag='garment',sides=24)
        # Rounded palm and connected overlapping tapered fingers, anatomical thumb
        # in the forward half-plane for BOTH mirrored hands.
        f=.80 if child else 1.10;hc=Vector((sign*(wrist+.051*f),0,sz))
        m.ball(hc,(.061*f,.032*f,.044*f),'skin',seg=24,rings=18,bones={hand:1})
        for k in range(3):
            z=(k-1)*.025*f
            pts=bezier([hc+Vector((sign*.033*f,0,z)),hc+Vector((sign*.10*f,-.004*f,z)),hc+Vector((sign*.103*f,-.018*f,z*.85))],10)
            rs=[.015*f*(1-.55*(i/9)**3) for i in range(10)];rs[-1]=.004*f
            tube(m,pts,rs,'skin',{hand:1},sides=12)
        pts=bezier([hc+Vector((-sign*.018*f,-.018*f,.024*f)),hc+Vector((sign*.011*f,-.067*f,.027*f)),hc+Vector((sign*.041*f,-.053*f,.025*f))],12)
        tube(m,pts,[.020*f*(1-.72*(i/11)**3) for i in range(12)],'skin',{hand:1},sides=12)
        if s.get('band') and sign==-1:
            tube(m,[(sign*(wrist-.04),0,sz),(sign*(wrist-.008),0,sz)],[(ar*1.02,ar*1.02)]*2,'umb_yellow',{lo:1},sides=24)
    # Continuous clothed pelvis closes the crotch between the leg tubes. This is
    # essential for believable trousers from low angles and seated poses.
    if not s.get('skirt') and not s.get('dress'):
        before_pelvis=set(m.bm.verts)
        m.ball((0,.012,s['hip']-.005),(s['hx']+.077,s['bodyd']*.87,.113 if not child else .075),
               s['pants'],seg=36,rings=22,bones=B(Hips=1))
        m.tags.setdefault('pants',[]).extend(set(m.bm.verts)-before_pelvis)
    # Legs exist under skirts in the master, with enough rings at knees.
    for sign,side in ((1,'Left'),(-1,'Right')):
        hx=s['hx'];hz=s['hip'];kz=s['knee'];az=s['ankle']
        top=hz+.035;bot=az-.01;shorthem=kz+.06
        lr=.047 if child else (.062 if s.get('sneakers') else .069)
        def legwg(co,side=side):return H._chain(-co.z,[(-(hz-.025),'Hips',side+'UpperLeg',.035),(-kz,side+'UpperLeg',side+'LowerLeg',.044),(-az,side+'LowerLeg',side+'Foot',.025)])
        rows=[(bot,lr*.75,lr*.78,0),(kz-.04,lr*.94,lr*.91,0),(kz+.04,lr,lr,.003),(top,lr*1.24,lr*1.23,.007)]
        if not s.get('skirt') and not s.get('dress'):
            r=.083 if child else (.10 if s.get('bodyw',.2)>.25 else .083)
            if s.get('shorts'):
                rows=[(bot,lr*.75,lr*.78,0),(kz,lr,lr,0),(shorthem-.006,lr,lr,0),(shorthem,r,r,0),(top,r*1.10,r*1.1,.004)]
            else: rows=[(bot,r*.78,r*.78,0),(bot+.025,r*.82,r*.82,0),(kz,r*.91,r*.9,0),(top,r*1.11,r*1.14,.005)]
        before=set(m.bm.verts)
        def col(a,z):
            return 'skin' if (s.get('skirt') or s.get('dress') or (s.get('shorts') and z<shorthem)) else s['pants']
        loft(m,rows,col,legwg,seg=28,step=.013,folds=.007)
        legverts=set(m.bm.verts)-before
        for v in legverts:v.co.x+=sign*hx
        if not s.get('skirt') and not s.get('dress'):
            m.tags.setdefault('pants',[]).extend(legverts)
        # Concealed upper-leg geometry is kept separately in the master, then
        # omitted from the delivery surface to prevent cloth/body intersections.
        if s.get('skirt') or s.get('dress'):
            cutoff=(.117 if s['hair']=='songkok' else .076) if s.get('skirt') else s['hem']-.010
            m.tags.setdefault('concealed_anatomy',[]).extend(v for v in legverts if v.co.z>cutoff)
        # Footwear sits exactly on z=0.
        fl=s['foot'];fw=.075 if child else (.122 if s.get('sneakers') or s['hair']=='songkok' else .098)
        if s.get('sneakers'):
            solecol='sole' if not s.get('police') and not s.get('suit') else 'suit_black'
            shoecol='sneaker' if solecol=='sole' else 'suit_black'
            m.ball((sign*hx,-fl*.24,.036),(fw,fl*.53,.036),solecol,seg=32,rings=14,bones={side+'Foot':1})
            m.ball((sign*hx,-fl*.18,.084),(fw*.94,fl*.45,.066),shoecol,seg=32,rings=20,bones={side+'Foot':1})
            if solecol=='sole':
                if s.get('shorts'):
                    tube(m,[(sign*hx,0,az-.018),(sign*hx,0,az+.046)],[(lr*.86,lr*.86)]*2,'dress_trim',
                         {side+'LowerLeg':.8,side+'Foot':.2},sides=20)
                m.ball((sign*hx,-fl*.50,.065),(fw*.92,fl*.20,.048),'dress_trim',seg=24,rings=16,bones={side+'Foot':1})
                for j in range(3):
                    yy=-fl*.24+j*.025;zz=.143-j*.005
                    stroke(m,[(sign*hx-fw*.44,yy,zz),(sign*hx,yy-.007,zz+.011),(sign*hx+fw*.44,yy,zz)],.004,'dress_trim',{side+'Foot':1},n=10)
        else:
            sandal='sandal_brown' if s.get('moustache') else 'slipper_blue'
            m.ball((sign*hx,-fl*.22,.017),(fw,fl*.51,.017),sandal,seg=28,rings=12,bones={side+'Foot':1})
            m.ball((sign*hx,-fl*.20,.067),(fw*.86,fl*.50,.048),'skin',seg=28,rings=16,bones={side+'Foot':1})
            for j in range(4):
                xx=sign*hx+(j-1.5)*fw*.37
                m.ball((xx,-fl*.61,.051),(fw*.19,.027,.022),'skin',seg=14,rings=10,bones={side+'Toes':1})
            pts=bezier([(sign*hx-fw*.9,-fl*.33,.036),(sign*hx-fw*.5,-fl*.34,.15),(sign*hx+fw*.5,-fl*.34,.15),(sign*hx+fw*.9,-fl*.33,.036)],22)
            tube(m,pts,[(.015,.017)]*22,sandal,{side+'Foot':1},sides=10)

def make_skirt(m,p,s):
    if not s.get('skirt'):return
    top=s['hip']+.035;low=.12 if s['hair']=='songkok' else .079
    w=s['bodyw']*.99;d=s['bodyd']*.94
    rows=[(low,w*1.02,d*1.04,0),(low+.02,w*1.02,d*1.04,0),(top-.035,w,d,0),(top,w*.98,d,0)]
    def wg(co):
        # The near half follows its own thigh/knee; a narrow centre strip bridges
        # the two. Hem follows shins during seated and high-knee poses.
        t=smooth((top-co.z)/.15)
        left=smooth(.5+co.x/(w*.30))
        lower=smooth((s['knee']+.055-co.z)/.13)
        weights={'Hips':1-t,'LeftUpperLeg':t*left*(1-lower),'RightUpperLeg':t*(1-left)*(1-lower),
                 'LeftLowerLeg':t*left*lower,'RightLowerLeg':t*(1-left)*lower}
        return {k:v for k,v in weights.items() if v>1e-8}
    def col(a,z):
        if s['pants'] in ('sarong_green','sarong_red'):
            return ('sarong_check' if s['pants']=='sarong_green' else 'sarong_red_check') if (z*14)%1<.12 or (a*9/math.pi)%1<.10 else s['pants']
        return s['pants']
    loft(m,rows,col,wg,seg=96,step=.009,folds=.016)

def fuse_garment(m,p,s):
    """Union torso and sleeve roots into a single soft garment surface.

    Rest-space weights/colour are transferred from the designed source. High
    density near bends survives reduction; no Armature modifier is applied.
    """
    chosen=set(m.tags.get('torso',[])+m.tags.get('garment',[]))
    fs=[f for f in m.bm.faces if all(v in chosen for v in f.verts)]
    if not fs:return
    orig=list(chosen);indices={v:i for i,v in enumerate(orig)}
    original_weights={v:w for v,w in m.weights}
    kd=KDTree(len(orig))
    for i,v in enumerate(orig):kd.insert(v.co,i)
    kd.balance()
    color_by_v={}
    for f in fs:
        uv=f.loops[0][m.uv].uv.copy()
        for v in f.verts:color_by_v[v]=uv
    mesh=bpy.data.meshes.new('garment_union');mesh.from_pydata([v.co[:] for v in orig],[],[[indices[v] for v in f.verts] for f in fs]);mesh.update()
    # Signed-volume union requires consistent closed-component winding first.
    # Loft cap fans otherwise leave little inverted ridges at sleeve intersections.
    oriented=bmesh.new();oriented.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(oriented,faces=list(oriented.faces))
    oriented.to_mesh(mesh);oriented.free()
    ob=bpy.data.objects.new('garment_union',mesh);bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active=ob;ob.select_set(True)
    # Keep evenly spaced quads through the bending sleeve. Decimating a denser
    # rest mesh makes long flat triangles that corrugate when its arms lower.
    spacing=.008 if s.get('child') else (.014 if s.get('skirt') else .012)
    rem=ob.modifiers.new('ContinuousShoulders','REMESH');rem.mode='VOXEL';rem.voxel_size=spacing;rem.use_smooth_shade=True
    bpy.ops.object.modifier_apply(modifier=rem.name)
    sm=ob.modifiers.new('SoftCloth','SMOOTH');sm.factor=.9;sm.iterations=6;bpy.ops.object.modifier_apply(modifier=sm.name)
    new=[]
    for v in ob.data.vertices:
        # Analytic weights across the union avoid nearest-neighbour ridges when
        # the sleeve folds down from T pose into a relaxed stance.
        new.append(vertex(m,v.co,garment_weights(p,v.co),'fused_garment'))
    cuts={};new_set=set(new);new_weights={v:w for v,w in m.weights if v in new_set}
    trim_z=s['hem']+.027
    def clip_trim(vs,above):
        result=[]
        for a,b in zip(vs,vs[1:]+vs[:1]):
            ia=(a.co.z>=trim_z) if above else (a.co.z<=trim_z)
            ib=(b.co.z>=trim_z) if above else (b.co.z<=trim_z)
            if ia:result.append(a)
            if ia!=ib:
                key=frozenset((a,b))
                if key not in cuts:
                    t=(trim_z-a.co.z)/(b.co.z-a.co.z)
                    wa,wb=new_weights[a],new_weights[b]
                    ws={k:mix(wa.get(k,0),wb.get(k,0),t) for k in set(wa)|set(wb)}
                    ws=dict(sorted(ws.items(),key=lambda kv:-kv[1])[:4]);total=sum(ws.values())
                    ws={k:v/total for k,v in ws.items() if v>1e-8}
                    cuts[key]=vertex(m,a.co.lerp(b.co,t),ws,'fused_garment')
                result.append(cuts[key])
        return result
    for f in ob.data.polygons:
        vs=[new[i] for i in f.vertices]
        if s.get('dress'):
            # Split the real garment at the colour seam. This stays watertight
            # under animation and avoids an intersecting overlay hem.
            for above,col in ((True,s['shirt']),(False,'dress_trim')):
                poly=clip_trim(vs,above)
                if len(poly)>=3:face(m,poly,col)
        else:
            nf=m.bm.faces.new(vs);nf.smooth=True
            _,idx,_=kd.find(f.center)
            for lp in nf.loops:lp[m.uv].uv=color_by_v[orig[idx]]
    bpy.data.objects.remove(ob,do_unlink=True)
    bmesh.ops.delete(m.bm,geom=list(chosen),context='VERTS')
    m.weights=[(v,w) for v,w in m.weights if v.is_valid]
    for tag,vs in m.tags.items():m.tags[tag]=[v for v in vs if v.is_valid]

def build(asset_id):
    s=SPECS[asset_id].copy();p=proportions(s)
    m=K.Mesher(detail=1.,smooth=True,lsegs=3)
    make_body(m,p,s);make_limbs(m,p,s);make_skirt(m,p,s)
    import kl_refined_pants as RP
    RP.fuse_pants(m,p,s)
    fuse_garment(m,p,s)
    make_head(m,p,s)
    import kl_refined_hair as RH
    RH.make_hair(m,p,s)
    degenerate=[f for f in m.bm.faces if f.calc_area()<1e-12]
    if degenerate:bmesh.ops.delete(m.bm,geom=degenerate,context='FACES_ONLY')
    bmesh.ops.recalc_face_normals(m.bm,faces=list(m.bm.faces))
    RH.orient_scarf_normals(m,s)
    for tag,sign in [('decal_front',-1),('decal_back',1)]:
        vs=set(m.tags.get(tag,[]))
        for f in m.bm.faces:
            if all(v in vs for v in f.verts) and f.normal.y*sign<0:f.normal_flip()
    vs=set(sum([m.tags.get(tag,[]) for tag in ['mouth','teeth','eye_L','eye_R']],[]))
    center=Vector((0,0,s['headc']))
    for f in m.bm.faces:
        if all(v in vs for v in f.verts) and f.normal.dot(f.calc_center_median()-center)<0:f.normal_flip()
    return m,p

BUILDERS={cid:(lambda cid=cid:build(cid)) for cid in SPECS}
FACE_KEYS=C.FACE_KEYS
