"""Builds the épée hilts, the fencing mask and the Strip textures with Blender's bpy and writes them into the Unity project.

Run from the repo root, no Blender install needed (PER-41):

    uv run --no-project --python 3.11 --with bpy==4.2.0 tools/models/build_models.py

Everything is generated from the numbers below (FIE m.17.1: the bell is at most 13.5 cm across and 3 to 5.5 cm deep),
so nothing is downloaded and nothing needs a licence. Output:

- unity/Assets/VrFencing/Blade/Unity/Resources/Hilts/EpeeHilt{French,Visconti,Belgian}.fbx: objects Bell, Pad,
  Collar, Grip, Prong (pistol grips), Pommel. Modelled in the blade's mount frame: the front of the guard at the origin,
  the blade along +Z (Unity), the thumb side +Y; the grips hang behind the guard along -Z. Right-handed; the left hand
  mirrors it in Unity.
- unity/Assets/VrFencing/Dummy/Unity/Resources/FencingMask.fbx: objects MaskMesh (the wire front), MaskBack (the
  cloth back) and MaskBib, around the head's center, the face toward +Z (Unity).
- unity/Assets/VrFencing/Strip/Unity/Resources/StripHatch.png: the Warning Zones' diagonal hatching.

Blender is Z up with -Y as the front, and Unity's FBX import turns that front into +Z, Blender +Z into +Y and Blender +X
into -X (its right-to-left-handed flip). The exporter's axis options only annotate the file, so the geometry is built in
that convention here (`frame`): "right" is Blender -X, "forward" (the blade, the face) is Blender -Y, "up" is Blender +Z.
The Edit-mode HiltModelTests check the result in Unity.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HILTS_DIR = os.path.join(
    REPO_ROOT, "unity", "Assets", "VrFencing", "Blade", "Unity", "Resources", "Hilts"
)
MASK_DIR = os.path.join(
    REPO_ROOT, "unity", "Assets", "VrFencing", "Dummy", "Unity", "Resources"
)
STRIP_DIR = os.path.join(
    REPO_ROOT, "unity", "Assets", "VrFencing", "Strip", "Unity", "Resources"
)

# FIE m.17.1: the bell is at most 13.5 cm across and 3 to 5.5 cm deep.
BELL_RADIUS = 0.0675
BELL_DEPTH = 0.041
BELL_SEGMENTS = 48
RING_SEGMENTS = 24


def frame(right, forward, up):
    """A point in the model frame (right, forward, up) as a Blender vector."""
    return Vector((-right, -forward, up))


def superellipse_ring(
    center,
    axis_a,
    axis_b,
    radius_a,
    radius_b,
    squareness=2.0,
    count=RING_SEGMENTS,
    arc=None,
):
    """A ring of points around center in the plane of axis_a and axis_b. squareness 2 is an ellipse, higher is boxier.
    arc=(start, end) in radians limits it to an open arc, inclusive at both ends."""
    points = []
    if arc is None:
        angles = [2.0 * math.pi * index / count for index in range(count)]
    else:
        start, end = arc
        angles = [start + (end - start) * index / count for index in range(count + 1)]
    for angle in angles:
        cosine, sine = math.cos(angle), math.sin(angle)
        a = math.copysign(abs(cosine) ** (2.0 / squareness), cosine) * radius_a
        b = math.copysign(abs(sine) ** (2.0 / squareness), sine) * radius_b
        points.append(center + axis_a * a + axis_b * b)
    return points


def loft(rings, closed=True, cap_start=False, cap_end=False, flip=False):
    """Vertices and faces joining consecutive rings of equal length with quads; caps fan to the ring's center."""
    verts = []
    faces = []
    count = len(rings[0])
    for ring in rings:
        verts.extend(ring)
    for ring_index in range(len(rings) - 1):
        base = ring_index * count
        for index in range(count if closed else count - 1):
            next_index = (index + 1) % count
            quad = [
                base + index,
                base + next_index,
                base + count + next_index,
                base + count + index,
            ]
            faces.append(quad[::-1] if flip else quad)
    if cap_start:
        center = sum(rings[0], Vector()) / count
        verts.append(center)
        center_index = len(verts) - 1
        for index in range(count if closed else count - 1):
            triangle = [index, center_index, (index + 1) % count]
            faces.append(triangle[::-1] if flip else triangle)
    if cap_end:
        center = sum(rings[-1], Vector()) / count
        verts.append(center)
        center_index = len(verts) - 1
        base = (len(rings) - 1) * count
        for index in range(count if closed else count - 1):
            triangle = [base + index, base + (index + 1) % count, center_index]
            faces.append(triangle[::-1] if flip else triangle)
    return verts, faces


def lathe(profile, axis, origin, side, count=BELL_SEGMENTS, closed_profile=True):
    """Spins a (radius, along) profile around axis through origin; side is any unit vector perpendicular to axis."""
    other = axis.cross(side).normalized()
    rings = [
        superellipse_ring(
            origin + axis * along, side, other, radius, radius, 2.0, count
        )
        for radius, along in profile
    ]
    if closed_profile:
        rings.append(rings[0])
    return loft(rings)


def make_object(name, verts, faces, flip=False):
    mesh = bpy.data.meshes.new(name)
    if flip:
        faces = [face[::-1] for face in faces]
    mesh.from_pydata([tuple(vertex) for vertex in verts], [], faces)
    mesh.update()
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def merge(parts):
    """Joins (verts, faces) lists into one."""
    verts, faces = [], []
    for part_verts, part_faces in parts:
        offset = len(verts)
        verts.extend(part_verts)
        faces.extend([[index + offset for index in face] for face in part_faces])
    return verts, faces


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export(objects, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="OFF",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False,
    )
    triangles = sum(
        sum(len(polygon.vertices) - 2 for polygon in obj.data.polygons)
        for obj in objects
    )
    print(
        f"[models] {os.path.relpath(path, REPO_ROOT)}: {len(objects)} objects, {triangles} triangles"
    )


# --- Hilts -------------------------------------------------------------------

RIGHT = Vector((-1.0, 0.0, 0.0))
FORWARD = Vector((0.0, -1.0, 0.0))
UP = Vector((0.0, 0.0, 1.0))


def bell():
    """The bell guard, dished: apex at the origin where the blade leaves it, the rim 13.5 cm across 4.1 cm behind, a
    rolled rim and a wall about 1.5 mm thick (outer surface, rim, inner surface, back to the blade hole)."""
    # A spherical cap 13.5 cm across and 4.1 cm deep has a radius of about 7.7 cm.
    cap_radius = ((BELL_RADIUS * BELL_RADIUS) + (BELL_DEPTH * BELL_DEPTH)) / (
        2.0 * BELL_DEPTH
    )

    def cap(depth):
        return math.sqrt((2.0 * cap_radius * depth) - (depth * depth))

    outer = [(0.0075, 0.0)] + [
        (cap(depth), -depth)
        for depth in (0.004, 0.011, 0.019, 0.028, 0.036, BELL_DEPTH)
    ]
    rim = [
        (BELL_RADIUS, -BELL_DEPTH - 0.003),
        (BELL_RADIUS - 0.0025, -BELL_DEPTH - 0.004),
    ]
    inner = [
        (cap(depth) - 0.0015, -depth)
        for depth in (0.0385, 0.0295, 0.0205, 0.0125, 0.0065)
    ] + [(0.009, -0.0025)]
    verts, faces = lathe(outer + rim + inner, FORWARD, Vector(), UP)
    return make_object("Bell", verts, faces)


def pad():
    """The cushion inside the bell, behind the blade hole."""
    profile = [
        (0.0075, -0.0035),
        (0.0255, -0.0085),
        (0.0295, -0.0125),
        (0.0295, -0.017),
        (0.0095, -0.017),
    ]
    verts, faces = lathe(profile, FORWARD, Vector(), UP, 32)
    return make_object("Pad", verts, faces)


def collar(length=0.012):
    """The socket between the bell and the grip."""
    profile = [
        (0.0, -0.040),
        (0.0115, -0.040),
        (0.0115, -0.040 - length),
        (0.0, -0.040 - length),
    ]
    verts, faces = lathe(profile, FORWARD, Vector(), UP, 24)
    return make_object("Collar", verts, faces)


def spine_rings(sections, squareness, side_offset=0.0):
    """Rings along a spine given as (forward, up, radius_side, radius_up) in the forward-up plane."""
    rings = []
    for index, (forward, up, radius_side, radius_up) in enumerate(sections):
        before = sections[max(0, index - 1)]
        after = sections[min(len(sections) - 1, index + 1)]
        tangent = frame(0.0, after[0] - before[0], after[1] - before[1]).normalized()
        # Perpendicular to the tangent, in the forward-up plane.
        normal = RIGHT.cross(tangent).normalized()
        if normal.z < 0.0:
            normal = -normal
        center = frame(side_offset, forward, up)
        rings.append(
            superellipse_ring(center, RIGHT, normal, radius_side, radius_up, squareness)
        )
    return rings


def pommel_lathe(start, direction, profile, count=32):
    verts, faces = lathe(profile, direction.normalized(), start, RIGHT, count)
    return verts, faces


def french_hilt():
    """A straight French grip: a rounded-rectangular bar tapering to the pommel, bending gently toward the palm."""
    parts = [bell(), pad(), collar()]
    sections = []
    length = 0.165
    for step in range(10):
        fraction = step / 9.0
        forward = -0.050 - length * fraction
        up = -0.010 * fraction * fraction
        sections.append(
            (forward, up, 0.0105 - 0.0025 * fraction, 0.0135 - 0.0025 * fraction)
        )
    rings = spine_rings(sections, 3.4)
    verts, faces = loft(rings, cap_start=True)
    parts.append(make_object("Grip", verts, faces))

    end = frame(0.0, sections[-1][0], sections[-1][1])
    before = frame(0.0, sections[-2][0], sections[-2][1])
    direction = (end - before).normalized()
    profile = [
        (0.0, 0.002),
        (0.0095, 0.002),
        (0.0125, -0.002),
        (0.0135, -0.010),
        (0.0135, -0.026),
        (0.0105, -0.033),
        (0.005, -0.036),
        (0.0, -0.037),
    ]
    verts, faces = pommel_lathe(end, direction, profile)
    parts.append(make_object("Pommel", verts, faces))
    return parts


def pistol_hilt(sections, prong, squareness, nut_radius):
    parts = [bell(), pad(), collar(0.008)]
    rings = spine_rings(sections, squareness, -0.002)
    verts, faces = loft(rings, cap_start=True, cap_end=True)
    parts.append(make_object("Grip", verts, faces))

    rings = spine_rings(prong, 2.4, 0.004)
    verts, faces = loft(rings, cap_start=True, cap_end=True)
    parts.append(make_object("Prong", verts, faces))

    end = frame(-0.002, sections[-1][0], sections[-1][1])
    before = frame(-0.002, sections[-2][0], sections[-2][1])
    direction = (end - before).normalized()
    profile = [
        (0.0, 0.002),
        (nut_radius, 0.002),
        (nut_radius, -0.007),
        (nut_radius * 0.6, -0.009),
        (0.0, -0.009),
    ]
    verts, faces = pommel_lathe(end, direction, profile, 24)
    parts.append(make_object("Pommel", verts, faces))
    return parts


def visconti_hilt():
    """A Visconti pistol grip: a short neck, a round palm body dropping back and down, a finger prong at the neck."""
    sections = [
        (-0.046, 0.000, 0.011, 0.012),
        (-0.058, -0.003, 0.013, 0.014),
        (-0.072, -0.011, 0.016, 0.018),
        (-0.088, -0.024, 0.018, 0.021),
        (-0.104, -0.038, 0.018, 0.021),
        (-0.118, -0.053, 0.016, 0.018),
        (-0.128, -0.066, 0.012, 0.013),
        (-0.133, -0.074, 0.007, 0.007),
    ]
    prong = [
        (-0.061, -0.002, 0.009, 0.010),
        (-0.063, -0.018, 0.0085, 0.009),
        (-0.066, -0.032, 0.0065, 0.007),
        (-0.068, -0.040, 0.003, 0.003),
    ]
    return pistol_hilt(sections, prong, 2.2, 0.009)


def belgian_hilt():
    """A Belgian pistol grip: longer and squarer, the body sloping back before it drops, a longer prong further back."""
    sections = [
        (-0.046, 0.000, 0.011, 0.012),
        (-0.060, -0.002, 0.014, 0.015),
        (-0.078, -0.008, 0.017, 0.018),
        (-0.096, -0.018, 0.019, 0.021),
        (-0.114, -0.031, 0.019, 0.022),
        (-0.130, -0.046, 0.017, 0.020),
        (-0.142, -0.061, 0.013, 0.015),
        (-0.148, -0.071, 0.007, 0.007),
    ]
    prong = [
        (-0.068, -0.004, 0.010, 0.011),
        (-0.071, -0.020, 0.009, 0.010),
        (-0.076, -0.036, 0.007, 0.008),
        (-0.079, -0.046, 0.003, 0.003),
    ]
    return pistol_hilt(sections, prong, 3.0, 0.009)


# --- Mask --------------------------------------------------------------------

MASK_SECTOR = math.radians(
    125.0
)  # the wire mesh covers this far round from the front on each side


def head_ring(up, front, back, side, arc=None, count=40):
    """A horizontal ring around the head's vertical axis: front and back depths, side half-width."""
    points = []
    if arc is None:
        angles = [2.0 * math.pi * index / count for index in range(count)]
    else:
        angles = [
            arc[0] + (arc[1] - arc[0]) * index / count for index in range(count + 1)
        ]
    for angle in angles:
        cosine, sine = math.cos(angle), math.sin(angle)
        depth = front if cosine >= 0.0 else back
        points.append(frame(side * sine, depth * cosine, up))
    return points


MASK_RINGS = [
    (-0.115, 0.080, 0.070, 0.080),
    (-0.090, 0.108, 0.086, 0.100),
    (-0.050, 0.122, 0.095, 0.106),
    (0.000, 0.126, 0.100, 0.108),
    (0.050, 0.122, 0.100, 0.106),
    (0.090, 0.110, 0.095, 0.098),
    (0.115, 0.085, 0.080, 0.082),
    (0.132, 0.042, 0.042, 0.042),
]


def mask_shell(front_sector):
    """The shell between the chin and the crown as two open lofts: the front sector (wire mesh) and the back (cloth)."""
    arc = (
        (-MASK_SECTOR, MASK_SECTOR)
        if front_sector
        else (MASK_SECTOR, 2.0 * math.pi - MASK_SECTOR)
    )
    rings = [
        head_ring(up, front, back, side, arc) for up, front, back, side in MASK_RINGS
    ]
    verts, faces = loft(rings, closed=False)
    # The crown: a fan from the last ring to the top center.
    top = frame(0.0, 0.0, 0.138)
    verts.append(top)
    base = (len(rings) - 1) * len(rings[0])
    for index in range(len(rings[0]) - 1):
        faces.append([base + index, base + index + 1, len(verts) - 1])
    return verts, faces


def mask_bib():
    """The bib hanging from the chin over the throat, 6 mm thick so both sides draw."""
    rows = [(-0.100, 1.00), (-0.140, 1.06), (-0.180, 1.13), (-0.215, 1.18)]
    chin_front, chin_back, chin_side = 0.104, 0.084, 0.098
    arc = (-math.radians(135.0), math.radians(135.0))
    outer = [
        head_ring(up, chin_front * scale, chin_back * scale, chin_side * scale, arc)
        for up, scale in rows
    ]
    inner = [
        head_ring(
            up,
            chin_front * scale - 0.006,
            chin_back * scale - 0.006,
            chin_side * scale - 0.006,
            arc,
        )
        for up, scale in rows
    ]
    outer_verts, outer_faces = loft(outer, closed=False)
    inner_verts, inner_faces = loft(inner, closed=False, flip=True)
    verts, faces = merge([(outer_verts, outer_faces), (inner_verts, inner_faces)])
    count = len(outer[0])
    inner_base = len(outer_verts)
    # The bottom edge and the two side edges close the two layers.
    bottom = (len(rows) - 1) * count
    for index in range(count - 1):
        faces.append(
            [
                bottom + index + 1,
                bottom + index,
                inner_base + bottom + index,
                inner_base + bottom + index + 1,
            ]
        )
    for row in range(len(rows) - 1):
        first = row * count
        faces.append(
            [first, first + count, inner_base + first + count, inner_base + first]
        )
        last = row * count + count - 1
        faces.append([last + count, last, inner_base + last, inner_base + last + count])
    return verts, faces


def mask():
    verts, faces = mask_shell(front_sector=True)
    parts = [make_object("MaskMesh", verts, faces)]
    verts, faces = mask_shell(front_sector=False)
    parts.append(make_object("MaskBack", verts, faces))
    verts, faces = mask_bib()
    parts.append(make_object("MaskBib", verts, faces))
    return parts


# --- Strip textures ------------------------------------------------------------


def write_hatch(path, size=64, period=16):
    """Diagonal stripes, half the texel covered, for the Warning Zones; tiled by the markings code."""
    image = bpy.data.images.new("StripHatch", size, size, alpha=True)
    pixels = []
    for y in range(size):
        for x in range(size):
            on = ((x + y) % period) < period // 2
            pixels.extend([1.0, 1.0, 1.0, 1.0 if on else 0.0])
    image.pixels = pixels
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print(f"[models] {os.path.relpath(path, REPO_ROOT)}: {size}x{size} hatch")


def main():
    clear_scene()
    for name, build in (
        ("EpeeHiltFrench", french_hilt),
        ("EpeeHiltVisconti", visconti_hilt),
        ("EpeeHiltBelgian", belgian_hilt),
    ):
        clear_scene()
        parts = build()
        export(parts, os.path.join(HILTS_DIR, name + ".fbx"))
    clear_scene()
    export(mask(), os.path.join(MASK_DIR, "FencingMask.fbx"))
    os.makedirs(STRIP_DIR, exist_ok=True)
    write_hatch(os.path.join(STRIP_DIR, "StripHatch.png"))


if __name__ == "__main__":
    main()
