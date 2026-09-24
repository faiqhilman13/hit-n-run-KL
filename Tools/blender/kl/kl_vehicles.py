"""KL vehicles: delivery bike (P0), taxi and food truck (P1).

Each vehicle is an empty root at the ground centre with named child parts whose origins
sit on their real pivots (wheel hubs, steering head, hatch hinges), plus a separate
simplified collision proxy named COL_<id> in the COLLISION collection.
Front = -Y while modelling (exported as Unity +Z)."""
import math
from mathutils import Vector

import kl_core as K

AX = (0, math.pi / 2, 0)   # cylinder axis along X (wheels)
VDETAIL = 2.0              # facet multiplier for round parts (wheels, lamps): triangle budget control


def wheel(m, c, r, w, tyre="rubber", hub="metal", hub_r=0.45, spokes=0):
    c = Vector(c)
    m.cyl(c, r, r, w, tyre, seg=14, rot=AX)
    for s in (1, -1):
        m.cyl(c + Vector((s * w * 0.5, 0, 0)), r * hub_r, r * hub_r * 0.85, 0.02, hub, seg=10, rot=(0, s * math.pi / 2, 0))
        m.cyl(c + Vector((s * (w * 0.5 + 0.012), 0, 0)), r * 0.14, r * 0.1, 0.02, "metal_dark", seg=6, rot=(0, s * math.pi / 2, 0))
    for k in range(spokes):
        a = k / spokes * math.pi
        d = Vector((0, math.cos(a), math.sin(a))) * r * 0.8
        m.tube(c - d, c + d, 0.008, 0.008, "metal", seg=4)


def delivery_bike():
    """veh_delivery_bike: teal/cream kapcai with a rear delivery box.
    Envelope ~1.85 x 0.70 x 1.15 m."""
    global VDETAIL
    VDETAIL = 3.0            # per-vehicle facet level (see stats.json for the resulting budget)
    parts = []
    wr, ww = 0.28, 0.13
    fz, rz = -0.64, 0.62                      # wheel hub Y (front is -Y)
    # ---------------- Body (frame, leg shield, floor, seat, engine, rear shell, box)
    b = K.Mesher(VDETAIL)
    # step-through floorboard + underbone
    b.box((0, -0.12, 0.34), (0.3, 0.52, 0.09), "teal_dark", bevel=0.02)
    b.tube((0, -0.42, 0.72), (0, -0.1, 0.36), 0.05, 0.05, "teal", seg=6)
    # cream leg shield wrapping the front of the rider's shins
    b.box((0, -0.40, 0.58), (0.5, 0.09, 0.62), "cream", taper=(0.7, 1.0), rot=(-0.35, 0, 0), bevel=0.025)
    b.box((0, -0.43, 0.84), (0.26, 0.16, 0.22), "teal", taper=(0.75, 0.9), rot=(-0.35, 0, 0), bevel=0.02)   # front cowl
    # engine + exhaust on the right side
    b.box((0, 0.1, 0.32), (0.24, 0.3, 0.22), "metal_dark", bevel=0.02)
    b.tube((-0.1, 0.02, 0.24), (-0.16, 0.5, 0.26), 0.035, 0.035, "metal_dark", seg=6)
    b.tube((-0.16, 0.45, 0.27), (-0.18, 0.82, 0.3), 0.06, 0.05, "metal_dark", seg=8)
    # bulbous rear body under the seat with cream side stripes
    b.box((0, 0.36, 0.58), (0.34, 0.66, 0.34), "teal", taper=(0.9, 0.9), bevel=0.06)
    for s in (1, -1):
        b.box((s * 0.172, 0.34, 0.58), (0.02, 0.5, 0.1), "cream")
    b.box((0, 0.3, 0.8), (0.3, 0.58, 0.1), "seat_black", bevel=0.035)           # saddle
    # rear fender, taillight, indicators, number plate
    b.box((0, 0.66, 0.6), (0.2, 0.36, 0.06), "teal", rot=(0.35, 0, 0), bevel=0.015)
    b.box((0, 0.72, 0.64), (0.12, 0.05, 0.07), "tail_red", bevel=0.01)
    for s in (1, -1):
        b.ball((s * 0.13, 0.7, 0.66), (0.035, 0.03, 0.03), "amber", seg=6, rings=4)
    b.box((0, 0.8, 0.5), (0.16, 0.02, 0.1), "sign_blank")
    # rear rack + delivery box (the lid is a separate hinged part)
    b.box((0, 0.66, 0.84), (0.36, 0.34, 0.03), "metal_dark")
    b.box((0, 0.68, 1.0), (0.46, 0.42, 0.3), "teal", bevel=0.02)
    b.box((0, 0.895, 0.99), (0.36, 0.02, 0.2), "pale_panel")                   # pale panel on the back
    for s in (1, -1):
        b.box((s * 0.12, 0.905, 1.1), (0.04, 0.02, 0.06), "strap")             # clasps
    # kickstand (parked)
    b.tube((0.08, 0.05, 0.24), (0.2, 0.12, 0.02), 0.015, 0.015, "metal_dark", seg=4)
    # footrests
    for s in (1, -1):
        b.box((s * 0.2, -0.02, 0.28), (0.12, 0.04, 0.03), "rubber")
    parts.append(("Body", b, None, None))

    # ---------------- Steering assembly (pivot on the steering head)
    head = Vector((0, -0.46, 0.86))
    st = K.Mesher(VDETAIL)
    st.tube(head + Vector((0.07, 0, 0.08)), Vector((0.07, fz, wr)), 0.025, 0.025, "metal", seg=6)
    st.tube(head + Vector((-0.07, 0, 0.08)), Vector((-0.07, fz, wr)), 0.025, 0.025, "metal", seg=6)
    st.box((0, fz - 0.02, wr + 0.2), (0.17, 0.42, 0.05), "teal", rot=(0.15, 0, 0), bevel=0.015)   # front mudguard
    st.box(head + Vector((0, -0.02, 0.18)), (0.2, 0.14, 0.12), "teal", bevel=0.02)             # handlebar cover
    st.tube(head + Vector((-0.33, 0.02, 0.22)), head + Vector((0.33, 0.02, 0.22)), 0.018, 0.018, "metal", seg=6)
    for s in (1, -1):
        st.tube(head + Vector((s * 0.24, 0.02, 0.22)), head + Vector((s * 0.34, 0.02, 0.22)), 0.03, 0.03, "rubber", seg=6)
        st.ball(head + Vector((s * 0.17, -0.07, 0.2)), (0.03, 0.025, 0.025), "amber", seg=6, rings=4)
        # mirror stalk + round mirror
        st.tube(head + Vector((s * 0.2, 0.0, 0.24)), head + Vector((s * 0.3, 0.02, 0.27)), 0.01, 0.01, "metal_dark", seg=4)
        mc = head + Vector((s * 0.31, 0.02, 0.29))
        st.cyl(mc, 0.06, 0.06, 0.02, "metal_dark", seg=10, rot=(math.pi / 2, 0, 0))
        st.cyl(mc + Vector((0, -0.012, 0)), 0.048, 0.048, 0.01, "glass", seg=10, rot=(math.pi / 2, 0, 0))
    # big round headlamp
    lc = head + Vector((0, -0.12, 0.16))
    st.cyl(lc, 0.085, 0.09, 0.05, "teal", seg=12, rot=(math.pi / 2, 0, 0))
    st.cyl(lc + Vector((0, -0.028, 0)), 0.068, 0.068, 0.012, "headlamp", seg=12, rot=(math.pi / 2, 0, 0))
    parts.append(("Steering", st, tuple(head), None))

    # ---------------- Wheels (front wheel rides on the steering assembly)
    fw = K.Mesher(VDETAIL)
    wheel(fw, (0, fz, wr), wr, ww, spokes=0)
    parts.append(("FrontWheel", fw, (0, fz, wr), "Steering"))
    rw = K.Mesher(VDETAIL)
    wheel(rw, (0, rz, wr), wr, ww * 1.1, spokes=0)
    parts.append(("RearWheel", rw, (0, rz, wr), None))

    # ---------------- Cargo lid, hinged at the front-top edge of the box
    hinge = Vector((0, 0.47, 1.15))
    lid = K.Mesher(VDETAIL)
    lid.box((0, 0.68, 1.165), (0.48, 0.44, 0.05), "teal_dark", bevel=0.012)
    lid.box((0, 0.9, 1.14), (0.1, 0.03, 0.05), "strap")
    parts.append(("CargoLid", lid, tuple(hinge), None))

    # ---------------- collision proxy (never rendered)
    col = K.Mesher(VDETAIL)
    col.box((0, 0.05, 0.55), (0.5, 1.8, 0.95), "rubber")
    parts.append(("COL_veh_delivery_bike", col, None, "__collision__"))
    return parts


# ------------------------------------------------------------------------------ shared car bits
def car_wheels(parts, hubs, r, w):
    """Four named wheels with their pivots on the hubs. hubs: {name: (x, y)}; vehicle right = -X."""
    for name, (x, y) in hubs.items():
        wm = K.Mesher(VDETAIL)
        wheel(wm, (x, y, r), r, w, hub="tyre_hub", hub_r=0.55)
        parts.append((name, wm, (x, y, r), None))


def arch(b, x, y, r, side):
    """Dark wheel-arch disc on the body side so the tyre reads as sitting in a well."""
    b.cyl((x, y, r * 1.2), r * 1.2, r * 1.2, 0.02, "bumper_black", seg=16, rot=(0, side * math.pi / 2, 0))  # sits on the ground line


def glass_panel(m, c, size, rot=(0, 0, 0)):
    m.box(c, size, "glass", rot=rot)


def round_lamp(m, c, r, color="headlamp", rim="metal", axis=-1):
    """Lamp facing along Y (axis -1 = front)."""
    c = Vector(c)
    m.cyl(c, r * 1.2, r * 1.2, 0.03, rim, seg=12, rot=(math.pi / 2, 0, 0))
    m.cyl(c + Vector((0, axis * 0.018, 0)), r, r, 0.02, color, seg=12, rot=(math.pi / 2, 0, 0))


# ------------------------------------------------------------------------------ taxi
def taxi():
    """veh_taxi: compact KL budget taxi - yellow body over a green lower band, roof light,
    round headlamps. ~3.55 x 1.55 x 1.55 m. Right-hand drive: driver door = Door_FR (-X)."""
    global VDETAIL
    VDETAIL = 4.0
    parts = []
    L, W = 3.35, 1.44
    hy = L / 2
    wr, ww = 0.3, 0.2
    fy, ry = -1.08, 1.08
    wx = W / 2 - ww / 2 + 0.035          # tyres stand slightly proud of the body side
    b = K.Mesher(VDETAIL)
    # lower body: green band + dark sills
    b.box((0, 0, 0.44), (W, L - 0.12, 0.3), "taxi_green", bevel=0.05)
    b.box((0, 0, 0.3), (W - 0.06, L - 0.3, 0.08), "bumper_black")
    # yellow upper body with a sloped bonnet and boot
    b.box((0, 0.05, 0.75), (W + 0.02, L - 0.3, 0.32), "taxi_yellow", bevel=0.06)
    b.box((0, -1.35, 0.8), (W - 0.04, 0.72, 0.18), "taxi_yellow", taper=(0.96, 0.8), rot=(0.08, 0, 0), bevel=0.05)  # bonnet
    # cabin / greenhouse
    cab_y, cab_d, cab_h, cab_z = 0.22, 1.9, 0.46, 0.91
    b.box((0, cab_y, cab_z + cab_h / 2), (W - 0.08, cab_d, cab_h), "taxi_yellow", taper=(0.88, 0.84), bevel=0.05)
    b.box((0, cab_y + 0.02, cab_z + cab_h + 0.01), (W * 0.76, cab_d * 0.8, 0.03), "taxi_yellow", bevel=0.01)   # roof skin
    lean_x, lean_y = math.atan((W - 0.08) * 0.06 / cab_h), math.atan(cab_d * 0.08 / cab_h)
    zc = cab_z + cab_h * 0.48
    glass_panel(b, (0, cab_y - cab_d / 2 + 0.06, zc), (W * 0.72, 0.03, cab_h * 0.78), rot=(-lean_y, 0, 0))      # windscreen
    glass_panel(b, (0, cab_y + cab_d / 2 - 0.06, zc), (W * 0.7, 0.03, cab_h * 0.7), rot=(lean_y, 0, 0))          # rear glass
    # interior hints through the glass: seats + headrests
    for x in (0.33, -0.33):
        b.box((x, 0.05, 1.0), (0.42, 0.12, 0.4), "interior")
        b.box((x, 0.1, 1.24), (0.22, 0.08, 0.12), "interior")
    # roof light (blank - no lettering)
    b.box((0, cab_y, cab_z + cab_h + 0.1), (0.5, 0.2, 0.15), "taxi_yellow", taper=(0.85, 0.8), bevel=0.02)
    b.box((0, cab_y - 0.1, cab_z + cab_h + 0.1), (0.36, 0.02, 0.07), "amber")
    b.box((0, cab_y + 0.1, cab_z + cab_h + 0.1), (0.36, 0.02, 0.07), "amber")
    # front: grille, lamps, indicators, bumper, plate (blank)
    b.box((0, -hy + 0.05, 0.62), (0.62, 0.05, 0.2), "bumper_black", bevel=0.02)
    for k in range(3):
        b.box((0, -hy + 0.02, 0.56 + k * 0.06), (0.56, 0.02, 0.018), "metal_dark")
    for s in (1, -1):
        round_lamp(b, (s * 0.5, -hy + 0.04, 0.68), 0.11)
        b.box((s * 0.52, -hy + 0.06, 0.45), (0.14, 0.04, 0.07), "amber", bevel=0.01)
    b.box((0, -hy + 0.02, 0.36), (W + 0.06, 0.18, 0.2), "bumper_black", bevel=0.04)
    b.box((0, -hy - 0.07, 0.36), (0.44, 0.02, 0.12), "sign_blank")
    # rear: tail lamps, bumper, plate, exhaust
    for s in (1, -1):
        b.box((s * 0.6, hy - 0.08, 0.75), (0.2, 0.05, 0.3), "tail_red", bevel=0.02)
        b.box((s * 0.6, hy - 0.06, 0.62), (0.2, 0.04, 0.06), "white")
    b.box((0, hy - 0.04, 0.36), (W + 0.06, 0.18, 0.2), "bumper_black", bevel=0.04)
    b.box((0, hy + 0.06, 0.7), (0.44, 0.02, 0.14), "bumper_black")
    b.tube((0.45, hy - 0.1, 0.26), (0.45, hy + 0.12, 0.26), 0.035, 0.035, "metal_dark", seg=8)
    # wing mirrors
    for s in (1, -1):
        b.box((s * (W / 2 + 0.05), -0.72, 0.98), (0.08, 0.1, 0.1), "bumper_black", bevel=0.02)
        b.box((s * (W / 2 + 0.05), -0.66, 0.98), (0.06, 0.02, 0.08), "glass")
    for s in (1, -1):
        for y in (fy, ry):
            arch(b, s * (W / 2 + 0.004), y, wr, s)
    parts.append(("Body", b, None, None))
    car_wheels(parts, {"Wheel_FL": (wx, fy), "Wheel_FR": (-wx, fy), "Wheel_RL": (wx, ry), "Wheel_RR": (-wx, ry)}, wr, ww)
    # doors: skins over the body side with their window, hinged at the front edge
    for name, side, y0, y1 in (("Door_FL", 1, -0.72, 0.28), ("Door_FR", -1, -0.72, 0.28),
                               ("Door_RL", 1, 0.3, 0.74), ("Door_RR", -1, 0.3, 0.74)):
        d = K.Mesher(VDETAIL)
        x = side * (W / 2 + 0.018)
        yc, dl = (y0 + y1) / 2, y1 - y0 - 0.03
        d.box((x, yc, 0.44), (0.03, dl, 0.28), "taxi_green")
        d.box((x, yc, 0.76), (0.03, dl, 0.34), "taxi_yellow")
        gx = side * ((W - 0.08) / 2 * (1 - 0.06 * 0.5) + 0.012)     # on the (inward-leaning) cabin side
        d.box((gx, yc, 1.14), (0.02, dl - 0.1, 0.32), "glass", rot=(0, side * lean_x, 0))
        d.box((x + side * 0.018, yc + 0.25, 0.8), (0.02, 0.12, 0.03), "bumper_black")   # handle
        parts.append((name, d, (x, y0, 0.7), None))
    # steering wheel on the right (driver) side, pivot at its hub
    sw = K.Mesher(VDETAIL)
    hub = Vector((-0.33, -0.42, 1.02))
    for i in range(12):
        a0, a1 = i / 12 * math.tau, (i + 1) / 12 * math.tau
        p0 = hub + Vector((math.cos(a0) * 0.18, math.sin(a0) * 0.18 * 0.45, math.sin(a0) * 0.18 * 0.9))
        p1 = hub + Vector((math.cos(a1) * 0.18, math.sin(a1) * 0.18 * 0.45, math.sin(a1) * 0.18 * 0.9))
        sw.tube(p0, p1, 0.02, 0.02, "bumper_black", seg=4)
    sw.tube(hub, hub + Vector((0, 0.25, -0.18)), 0.03, 0.03, "bumper_black", seg=6)
    parts.append(("SteeringWheel", sw, tuple(hub), None))
    col = K.Mesher()
    col.box((0, 0, 0.8), (W, L, 1.0), "rubber")
    parts.append(("COL_veh_taxi", col, None, "__collision__"))
    return parts


# ------------------------------------------------------------------------------ food truck
def food_truck():
    """veh_food_truck: red cab-over hawker truck with a striped awning over a side service
    hatch (kerb side = vehicle's left, +X). ~3.95 x 1.8 x 2.15 m."""
    global VDETAIL
    VDETAIL = 3.2
    parts = []
    L, W = 3.85, 1.74
    hy = L / 2
    wr, ww = 0.33, 0.24
    fy, ry = -1.3, 1.15
    wx = W / 2 - ww / 2 + 0.035
    b = K.Mesher(VDETAIL)
    # chassis, sills, bumpers
    b.box((0, 0.1, 0.42), (W - 0.12, L - 0.3, 0.22), "bumper_black")
    b.box((0, -hy + 0.1, 0.4), (W + 0.04, 0.2, 0.24), "bumper_black", bevel=0.04)
    b.box((0, hy - 0.08, 0.42), (W, 0.16, 0.2), "bumper_black", bevel=0.03)
    # cab (cab-over), windscreen, grille, lamps
    cy0, cy1 = -hy + 0.05, -0.62
    cyc, cd = (cy0 + cy1) / 2, cy1 - cy0
    b.box((0, cyc, 1.08), (W - 0.04, cd, 1.2), "truck_red", taper=(0.97, 0.9), bevel=0.07)
    lean = math.atan(cd * 0.05 / 1.2)                     # the cab front leans back with its taper
    b.box((0, cy0 + 0.032, 1.36), (W * 0.84, 0.03, 0.5), "glass", rot=(-lean, 0, 0))        # windscreen
    b.box((0, cy0 + 0.03, 0.72), (0.56, 0.04, 0.14), "bumper_black", bevel=0.015)
    for k in range(3):
        b.box((0, cy0 + 0.0, 0.68 + k * 0.045), (0.5, 0.02, 0.016), "metal_dark")
    for s in (1, -1):
        round_lamp(b, (s * 0.58, cy0 + 0.01, 0.74), 0.11)
        b.ball((s * 0.8, cy0 + 0.05, 0.76), (0.05, 0.04, 0.05), "amber", seg=8, rings=5)
        b.box((s * 0.95, cy0 + 0.35, 1.3), (0.08, 0.1, 0.22), "bumper_black", bevel=0.02)  # mirror
        b.box((s * 0.95, cy0 + 0.29, 1.3), (0.06, 0.02, 0.18), "glass")
        b.tube((s * 0.86, cy0 + 0.36, 1.18), (s * 0.93, cy0 + 0.36, 1.22), 0.012, 0.012, "metal_dark", seg=4)
        b.box((s * 0.4, cy0 + 0.12, 1.66), (0.02, 0.16, 0.02), "bumper_black", rot=(0, 0, s * 0.3))  # wipers
    b.box((0, cy0 - 0.02, 0.4), (0.44, 0.02, 0.12), "sign_blank")                           # plate (blank)
    # cargo box
    by0, by1 = -0.56, hy - 0.12
    byc, bd = (by0 + by1) / 2, by1 - by0
    bz0, bz1 = 0.55, 2.08
    b.box((0, byc, (bz0 + bz1) / 2), (W, bd, bz1 - bz0), "truck_red", bevel=0.04)
    b.box((0, byc, bz1 + 0.015), (W - 0.06, bd - 0.06, 0.03), "truck_red")                # roof lip
    # service opening on the kerb side (+X): dark interior, counter shelf, pots, bottles
    ox = W / 2 + 0.005
    oy0, oy1, oz0, oz1 = -0.35, 1.25, 1.02, 1.82
    oyc, od = (oy0 + oy1) / 2, oy1 - oy0
    b.box((ox, oyc, (oz0 + oz1) / 2), (0.03, od, oz1 - oz0), "interior")
    b.box((ox - 0.12, oyc, oz0 + 0.3), (0.02, od - 0.1, 0.45), "pot_steel")                  # back splash
    b.box((ox + 0.1, oyc, oz0), (0.3, od + 0.1, 0.05), "wood", bevel=0.01)                   # counter shelf
    for s in (-1, 1):
        b.tube((ox + 0.0, oyc + s * od * 0.45, oz0 - 0.02), (ox + 0.2, oyc + s * od * 0.45, oz0 - 0.22), 0.015, 0.015,
               "metal_dark", seg=4)
    for i, y in enumerate((-0.15, 0.2, 0.5)):
        b.cyl((ox - 0.02, y, oz0 + 0.12), 0.12, 0.12, 0.2, "pot_steel", seg=12)
        b.cyl((ox - 0.02, y, oz0 + 0.23), 0.125, 0.1, 0.03, "metal_dark", seg=12)
    for y, col in ((0.8, "tail_red"), (0.88, "mustard"), (0.96, "tail_red")):
        b.cyl((ox + 0.06, y, oz0 + 0.11), 0.03, 0.022, 0.18, col, seg=8)
    b.box((ox + 0.05, 1.08, oz0 + 0.07), (0.1, 0.1, 0.1), "pot_steel")
    for k in range(4):
        b.tube((ox + 0.05 + (k - 1.5) * 0.015, 1.08, oz0 + 0.1), (ox + 0.05 + (k - 1.5) * 0.02, 1.08, oz0 + 0.22),
               0.005, 0.005, "wood", seg=4)
    # rear: doors seam, tail lamps
    b.box((0, hy - 0.1, 1.35), (0.02, 0.03, 1.4), "bumper_black")
    for s in (1, -1):
        b.box((s * 0.72, hy - 0.08, 0.72), (0.18, 0.06, 0.14), "tail_red", bevel=0.015)
        b.box((s * 0.52, hy - 0.08, 0.72), (0.14, 0.06, 0.14), "amber", bevel=0.015)
        b.box((s * 0.3, hy - 0.1, 1.35), (0.04, 0.03, 0.1), "metal_dark")                    # door handles
    b.tube((-0.5, hy - 0.3, 0.3), (-0.5, hy + 0.1, 0.3), 0.04, 0.04, "metal_dark", seg=8)    # exhaust
    for s in (1, -1):
        for y in (fy, ry):
            arch(b, s * (W / 2 + 0.004), y, wr, s)
    parts.append(("Body", b, None, None))
    car_wheels(parts, {"Wheel_FL": (wx, fy), "Wheel_FR": (-wx, fy), "Wheel_RL": (wx, ry), "Wheel_RR": (-wx, ry)}, wr, ww)
    # service hatch: red flap hinged along the top of the opening, propped open (up and out)
    hinge = Vector((ox + 0.01, oyc, oz1 + 0.02))
    h = K.Mesher(VDETAIL)
    h.box((ox + 0.3, oyc, oz1 + 0.02 + 0.05), (0.62, od + 0.06, 0.035), "truck_red", rot=(0, -0.2, 0), bevel=0.01)
    for s in (-1, 1):
        h.tube((ox + 0.02, oyc + s * od * 0.46, oz0 + 0.1), (ox + 0.5, oyc + s * od * 0.46, oz1 + 0.12), 0.012, 0.012,
               "metal", seg=4)                                                                # gas struts
    parts.append(("Hatch", h, tuple(hinge), None))
    # striped awning above the hatch, hinged at the roof edge, scalloped front edge
    ah = Vector((ox + 0.01, oyc, bz1 - 0.02))
    a = K.Mesher(VDETAIL)
    n = 9
    sw = (od + 0.3) / n
    for i in range(n):
        y = oyc - (od + 0.3) / 2 + (i + 0.5) * sw
        colr = "truck_red" if i % 2 == 0 else "awning_white"
        a.box((ox + 0.4, y, bz1 - 0.12), (0.78, sw, 0.03), colr, rot=(0, 0.28, 0))
        a.prism((ox + 0.79, y, bz1 - 0.23), (sw, 0.03, 0.1), colr, rot=(0, math.pi, math.pi / 2))
    parts.append(("Awning", a, tuple(ah), None))
    # cab doors (hinged at the front edge) - the right one is the driver's
    for name, side in (("Door_FL", 1), ("Door_FR", -1)):
        d = K.Mesher(VDETAIL)
        x = side * (W / 2 + 0.012)
        d.box((x, cyc + 0.05, 0.98), (0.03, cd - 0.2, 0.62), "truck_red")
        d.box((x, cyc + 0.05, 1.42), (0.03, cd - 0.3, 0.3), "glass")
        d.box((x + side * 0.018, cyc + 0.2, 1.05), (0.02, 0.1, 0.03), "bumper_black")
        parts.append((name, d, (x, cy0 + 0.15, 1.0), None))
    sw_m = K.Mesher(VDETAIL)
    hub = Vector((-0.4, cy0 + 0.45, 1.2))
    for i in range(12):
        a0, a1 = i / 12 * math.tau, (i + 1) / 12 * math.tau
        p0 = hub + Vector((math.cos(a0) * 0.19, math.sin(a0) * 0.19 * 0.3, math.sin(a0) * 0.19 * 0.95))
        p1 = hub + Vector((math.cos(a1) * 0.19, math.sin(a1) * 0.19 * 0.3, math.sin(a1) * 0.19 * 0.95))
        sw_m.tube(p0, p1, 0.02, 0.02, "bumper_black", seg=4)
    parts.append(("SteeringWheel", sw_m, tuple(hub), None))
    col = K.Mesher()
    col.box((0, 0.05, 1.25), (W, L, 1.75), "rubber")
    parts.append(("COL_veh_food_truck", col, None, "__collision__"))
    return parts


def assemble(asset_id, parts, mat, cols):
    """Create the root empty + parts. Returns (root, [export objects], stats)."""
    import bpy
    root = bpy.data.objects.new(asset_id, None)
    root.empty_display_size = 0.3
    root["kl_world_origin"] = [0, 0, 0]
    cols["EXPORT"].objects.link(root)
    made = {}
    tris = 0
    for name, m, origin, parent in parts:
        tris += m.tris() if parent != "__collision__" else 0
        if parent == "__collision__":
            ob, _, _ = m.to_object(name, mat, cols["COLLISION"], origin=origin, parent=root, turn180=True)
            ob.display_type = 'WIRE'
            ob.hide_render = True
        else:
            par = made.get(parent, root) if parent else root
            ob, _, _ = m.to_object(name, mat, cols["EXPORT"], origin=origin, parent=par, turn180=True)
        made[name] = ob
    return root, list(made.values()), tris
