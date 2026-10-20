"""Builds the épée hilts and the fencing mask (PER-41) with Blender's Python module and exports them as FBX into the
Unity project.

Run from the repo root (no Blender install needed; uv fetches the bpy wheel into a throwaway environment):

    uv run --python 3.11 --with bpy==4.2.0 python tools/models/build_fencing_models.py

Every model is written in Unity's frame (metres, +x right, +y up, +z forward) through `to_blender`, so the numbers here
read the same as in the C# that places them:

- Hilts: origin at the front of the bell guard (where the blade leaves it), blade along +z, the thumb's side of the
  grip up (+y), for a right hand (the left hand mirrors x at runtime). Each hilt is three objects, one per material:
  "Steel" (bell, pommel or nut, socket pins), "Grip" (handle, prongs, socket body) and "Pad" (the cushion in the bell).
- Mask: origin at the head's centre, the face looking along +z, sized for a head sphere of `MASK_HEAD_RADIUS`; the
  Dummy scales it to its own head. Two objects: "MaskMesh" (the wire front, textured with the mesh grid) and
  "MaskFabric" (the padded trim, bib and the back strap).

FIE sizes (Rules for competitions, material, m.25 to m.27): épée blade at most 90 cm, bell at most 13.5 cm across and
3 to 5.5 cm deep, the whole weapon at most 110 cm, so guard, grip and pommel together at most 20 cm.

The mask's wire grid texture is written next to the models as a PNG (pure Python, no image library).
"""

import math
import os
import struct
import sys
import zlib


import bpy

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BLADE_MODELS_DIR = os.path.join(
    REPO_ROOT,
    "unity",
    "Assets",
    "VrFencing",
    "Blade",
    "Unity",
    "Resources",
    "BladeModels",
)
DUMMY_MODELS_DIR = os.path.join(
    REPO_ROOT,
    "unity",
    "Assets",
    "VrFencing",
    "Dummy",
    "Unity",
    "Resources",
    "DummyModels",
)
STRIP_TEXTURES_DIR = os.path.join(REPO_ROOT, "unity", "Assets", "VrFencing", "Strip", "Unity", "Textures")
DUMMY_TEXTURES_DIR = os.path.join(
    REPO_ROOT, "unity", "Assets", "VrFencing", "Dummy", "Unity", "Textures"
)

# The bell: FIE limit 13.5 cm across, 3 to 5.5 cm deep. BladePose.GuardDepthMeters is 4 cm.
BELL_RADIUS = 0.066
BELL_DEPTH = 0.040
BELL_WALL = 0.0012
# The cushion inside the bell and the body-wire socket on it.
PAD_RADIUS = 0.047
PAD_FRONT_Z = -0.017
PAD_BACK_Z = -0.026
# Everything behind the bell ends by 16 cm behind its rim: 4 + 16 = 20 cm with the 90 cm blade, 110 cm in all.
HILT_END_Z = -0.199

MASK_HEAD_RADIUS = 0.11


def to_blender(point):
    """Unity (x right, y up, z forward, left-handed) to Blender (x right, y forward-in, z up) for the default FBX export
    (forward -Z, up Y, space transform baked), which Unity reads back with x mirrored."""
    x, y, z = point
    return (-x, -z, y)


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def lerp(a, b, t):
    return a + ((b - a) * t)


def normalize(v):
    length = math.sqrt(sum(c * c for c in v))
    return tuple(c / length for c in v) if length > 1e-12 else (0.0, 0.0, 0.0)


def cross(a, b):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def scale(a, s):
    return tuple(x * s for x in a)


def superellipse(angle, half_width, half_height, power):
    """A point on a superellipse |x/a|^p + |y/b|^p = 1: power 2 is an ellipse, higher is boxier with round corners."""
    c, s = math.cos(angle), math.sin(angle)
    exponent = 2.0 / power
    return (
        half_width * math.copysign(abs(c) ** exponent, c),
        half_height * math.copysign(abs(s) ** exponent, s),
    )


class MeshBuilder:
    """Collects vertices (Unity frame) and faces, then makes one Blender object."""

    def __init__(self):
        self.vertices = []
        self.faces = []
        self.uvs = {}

    def add_vertex(self, point, uv=None):
        self.vertices.append(to_blender(point))
        if uv is not None:
            self.uvs[len(self.vertices) - 1] = uv
        return len(self.vertices) - 1

    def add_loft(
        self,
        sections,
        segments,
        side_reference=(1.0, 0.0, 0.0),
        cap_start=True,
        cap_end=True,
    ):
        """Rings of superellipses along a path. Each section: (centre, half_width, half_height, power). half_width lies
        along `side_reference` (projected square to the path), half_height along the path's own up."""
        rings = []
        count = len(sections)
        for index, (centre, half_width, half_height, power) in enumerate(sections):
            before = sections[max(0, index - 1)][0]
            after = sections[min(count - 1, index + 1)][0]
            tangent = normalize(tuple(a - b for a, b in zip(after, before)))
            side = normalize(
                add(side_reference, scale(tangent, -dot(side_reference, tangent)))
            )
            up = cross(tangent, side)
            ring = []
            for segment in range(segments):
                angle = (2.0 * math.pi * segment) / segments
                along_side, along_up = superellipse(
                    angle, half_width, half_height, power
                )
                ring.append(
                    self.add_vertex(
                        add(centre, add(scale(side, along_side), scale(up, along_up)))
                    )
                )
            rings.append(ring)

        for ring_index in range(count - 1):
            this_ring, next_ring = rings[ring_index], rings[ring_index + 1]
            for segment in range(segments):
                following = (segment + 1) % segments
                self.faces.append(
                    (
                        this_ring[segment],
                        this_ring[following],
                        next_ring[following],
                        next_ring[segment],
                    )
                )

        if cap_start:
            self.faces.append(tuple(reversed(rings[0])))
        if cap_end:
            self.faces.append(tuple(rings[-1]))
        return rings

    def add_box(self, centre, size):
        cx, cy, cz = centre
        hx, hy, hz = (s * 0.5 for s in size)
        corners = [
            self.add_vertex((cx + sx * hx, cy + sy * hy, cz + sz * hz))
            for sx in (-1, 1)
            for sy in (-1, 1)
            for sz in (-1, 1)
        ]
        # Corner index: (sx, sy, sz) -> 4*ix + 2*iy + iz.
        quads = [
            (0, 1, 3, 2),
            (4, 6, 7, 5),
            (0, 4, 5, 1),
            (2, 3, 7, 6),
            (0, 2, 6, 4),
            (1, 5, 7, 3),
        ]
        for quad in quads:
            self.faces.append(tuple(corners[i] for i in quad))

    def build(self, name, smooth=True, smooth_angle_degrees=50.0):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        if self.uvs:
            layer = mesh.uv_layers.new(name="UVMap")
            for polygon in mesh.polygons:
                for loop_index in polygon.loop_indices:
                    vertex = mesh.loops[loop_index].vertex_index
                    layer.data[loop_index].uv = self.uvs.get(vertex, (0.0, 0.0))

        # White vertex colours: the shader multiplies by them, so a mesh without any would draw black on some GPUs.
        colors = mesh.color_attributes.new(
            name="Color", type="BYTE_COLOR", domain="CORNER"
        )
        for item in colors.data:
            item.color = (1.0, 1.0, 1.0, 1.0)

        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)

        # Merge coincident vertices and point every face outward (the Unity-to-Blender mirror flips the winding). bmesh
        # only imports once bpy is loaded.
        import bmesh
        work = bmesh.new()
        work.from_mesh(mesh)
        bmesh.ops.remove_doubles(work, verts=work.verts, dist=1e-6)
        bmesh.ops.recalc_face_normals(work, faces=work.faces)
        work.to_mesh(mesh)
        work.free()

        if smooth:
            mesh.shade_smooth()
            bpy.context.view_layer.objects.active = obj
            obj.select_set(True)
            bpy.ops.object.shade_smooth_by_angle(
                angle=math.radians(smooth_angle_degrees)
            )
            obj.select_set(False)
        else:
            mesh.shade_flat()
        return obj


def join(objects, name):
    """Joins objects into one (one renderer, one draw call per material in Unity)."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = name
    joined.data.name = name
    joined.select_set(False)
    return joined


# ---------------------------------------------------------------------------------------------------------------- hilt


def build_bell():
    """The bell: a spherical cap, apex at z = 0 (the blade's exit), rim BELL_DEPTH behind it, with a rolled lip."""
    sphere_radius = ((BELL_RADIUS**2) + (BELL_DEPTH**2)) / (2.0 * BELL_DEPTH)
    rim_angle = math.asin(BELL_RADIUS / sphere_radius)
    segments, rings = 40, 9
    builder = MeshBuilder()
    centre_z = -sphere_radius

    def cap_point(radius, polar, azimuth):
        return (
            radius * math.sin(polar) * math.cos(azimuth),
            radius * math.sin(polar) * math.sin(azimuth),
            centre_z + radius * math.cos(polar),
        )

    outer, inner = [], []
    for ring in range(rings + 1):
        polar = rim_angle * (0.035 + 0.965 * ring / rings)
        outer.append(
            [
                builder.add_vertex(
                    cap_point(sphere_radius, polar, 2 * math.pi * s / segments)
                )
                for s in range(segments)
            ]
        )
        inner.append(
            [
                builder.add_vertex(
                    cap_point(
                        sphere_radius - BELL_WALL, polar, 2 * math.pi * s / segments
                    )
                )
                for s in range(segments)
            ]
        )

    for ring in range(rings):
        for s in range(segments):
            n = (s + 1) % segments
            builder.faces.append(
                (outer[ring][s], outer[ring][n], outer[ring + 1][n], outer[ring + 1][s])
            )
            builder.faces.append(
                (inner[ring][s], inner[ring + 1][s], inner[ring + 1][n], inner[ring][n])
            )

    # The small hole round the tang, closed between the two walls.
    for s in range(segments):
        n = (s + 1) % segments
        builder.faces.append((outer[0][s], inner[0][s], inner[0][n], outer[0][n]))

    # The rolled lip: a half-round from the outer wall to the inner one, a little proud of both.
    lip_rings = []
    lip_radius = 0.0021
    rim_centre_radius = sphere_radius - (BELL_WALL * 0.5)
    for step in range(7):
        t = step / 6.0
        bulge = math.pi * t
        ring = []
        for s in range(segments):
            azimuth = 2 * math.pi * s / segments
            base = cap_point(rim_centre_radius, rim_angle, azimuth)
            outward = normalize((math.cos(azimuth), math.sin(azimuth), 0.0))
            along_wall = normalize(
                tuple(b - c for b, c in zip(base, (0.0, 0.0, centre_z)))
            )
            offset = add(
                scale(along_wall, lip_radius * math.cos(bulge)),
                scale(outward, lip_radius * 0.6 * math.sin(bulge)),
            )
            offset = add(offset, (0.0, 0.0, -lip_radius * 0.8 * math.sin(bulge)))
            ring.append(builder.add_vertex(add(base, offset)))
        lip_rings.append(ring)
    for step in range(6):
        for s in range(segments):
            n = (s + 1) % segments
            builder.faces.append(
                (
                    lip_rings[step][s],
                    lip_rings[step][n],
                    lip_rings[step + 1][n],
                    lip_rings[step + 1][s],
                )
            )
    for s in range(segments):
        n = (s + 1) % segments
        builder.faces.append(
            (outer[rings][s], outer[rings][n], lip_rings[0][n], lip_rings[0][s])
        )
        builder.faces.append(
            (inner[rings][s], lip_rings[6][s], lip_rings[6][n], inner[rings][n])
        )
    return builder.build("Bell", smooth=True, smooth_angle_degrees=70.0)


def build_pad():
    """The cushion inside the bell: a soft disc."""
    builder = MeshBuilder()
    sections = []
    profile = [
        (0.0, 0.82),
        (0.15, 0.96),
        (0.35, 1.0),
        (0.65, 1.0),
        (0.85, 0.96),
        (1.0, 0.8),
    ]
    for t, radius_fraction in profile:
        z = lerp(PAD_FRONT_Z, PAD_BACK_Z, t)
        radius = PAD_RADIUS * radius_fraction
        sections.append(((0.0, 0.0, z), radius, radius, 2.0))
    builder.add_loft(sections, 32)
    return builder.build("Pad", smooth=True, smooth_angle_degrees=80.0)


def build_socket(builder_grip, builder_steel):
    """The body-wire socket on the pad, below the grip (two pins and the clip, as on a 2-pin épée socket)."""
    socket_centre = (0.0, -0.030, PAD_BACK_Z - 0.008)
    builder_grip.add_box(socket_centre, (0.026, 0.013, 0.016))
    for x in (-0.0095, 0.0095):
        builder_steel.add_loft(
            [
                ((x, -0.030, PAD_BACK_Z - 0.016), 0.0022, 0.0022, 2.0),
                ((x, -0.030, PAD_BACK_Z - 0.026), 0.0022, 0.0022, 2.0),
            ],
            8,
        )
    builder_steel.add_loft(
        [
            ((0.0, -0.030, PAD_BACK_Z - 0.016), 0.0016, 0.0016, 2.0),
            ((0.0, -0.030, PAD_BACK_Z - 0.024), 0.0016, 0.0016, 2.0),
        ],
        8,
    )


def build_french_grip(grip, steel):
    """The French grip: a straight, slightly cambered handle of rounded-rectangle section, leather-wrapped, ending in
    a weighted pear-shaped pommel that screws onto the tang."""
    grip_front_z, grip_back_z = PAD_BACK_Z, -0.155
    sections = []
    for step in range(13):
        t = step / 12.0
        z = lerp(grip_front_z, grip_back_z, t)
        # A gentle camber down toward the pommel, and a taper.
        y = -0.0035 * (t**2)
        flare = 0.0015 * max(0.0, 1.0 - (t / 0.12))
        half_width = lerp(0.0112, 0.0092, t) + flare
        half_height = lerp(0.0138, 0.0112, t) + flare
        sections.append(((0.0, y, z), half_width, half_height, 2.8))
    grip.add_loft(sections, 20)

    # Metal ferrule at the front and the pommel at the back.
    steel.add_loft(
        [
            ((0.0, 0.0, grip_front_z + 0.001), 0.0128, 0.0152, 2.8),
            ((0.0, 0.0, grip_front_z - 0.006), 0.0128, 0.0152, 2.8),
        ],
        20,
    )
    pommel_profile = [
        (0.000, 0.0080),
        (0.004, 0.0105),
        (0.010, 0.0150),
        (0.018, 0.0168),
        (0.027, 0.0160),
        (0.035, 0.0128),
        (0.041, 0.0075),
        (0.044, 0.0030),
    ]
    pommel_start = grip_back_z + 0.001
    pommel_sections = [
        ((0.0, -0.0035, pommel_start - d), r, r, 2.0) for d, r in pommel_profile
    ]
    steel.add_loft(pommel_sections, 24)
    assert pommel_start - pommel_profile[-1][0] >= HILT_END_Z - 1e-4


def add_prong(grip, path, half_widths, half_thicknesses):
    """A finger prong: a loft along `path`, flattened front to back."""
    sections = [
        (point, w, t, 2.2) for point, w, t in zip(path, half_widths, half_thicknesses)
    ]
    # The ring's width lies along x; its thickness along the path's up, i.e. roughly along z for a downward prong.
    grip.add_loft(sections, 14)


def build_pistol_body(grip, profile):
    """The handle of a pistol grip from its side profile: (z, top y, bottom y, half width, squareness) per section.
    The tang runs along y = 0 through the upper part; the bottom drops into the palm swell and the tail."""
    sections = [((0.0, (top + bottom) * 0.5, z), half_width, (top - bottom) * 0.5, power) for z, top, bottom, half_width, power in profile]
    grip.add_loft(sections, 20)


def build_hex_nut(steel, front_z, back_z, radius):
    """The pommel nut of a pistol grip, a hexagon round the tang at y = 0, with the tang's end showing behind it."""
    steel.add_loft(
        [
            ((0.0, 0.0, front_z), radius, radius, 2.0),
            ((0.0, 0.0, back_z), radius, radius, 2.0),
        ],
        6,
    )
    steel.add_loft(
        [
            ((0.0, 0.0, back_z), radius * 0.55, radius * 0.55, 2.0),
            ((0.0, 0.0, back_z - 0.004), radius * 0.45, radius * 0.45, 2.0),
        ],
        10,
    )


def build_visconti(grip, steel):
    """The Visconti: a slim pistol grip with two slender curved prongs (the index finger hooks in front of the first,
    the middle finger in front of the second), a flat thumb rest on top and a long tail that curls down onto the wrist."""
    build_pistol_body(
        grip,
        [
            (PAD_BACK_Z, 0.0105, -0.0170, 0.0118, 2.8),
            (-0.034, 0.0100, -0.0215, 0.0116, 2.8),
            (-0.048, 0.0094, -0.0255, 0.0120, 3.0),
            (-0.066, 0.0090, -0.0280, 0.0124, 3.0),
            (-0.086, 0.0088, -0.0295, 0.0125, 3.0),
            (-0.104, 0.0086, -0.0310, 0.0121, 3.0),
            (-0.118, 0.0084, -0.0350, 0.0115, 2.8),
            (-0.128, 0.0078, -0.0415, 0.0109, 2.6),
            (-0.136, 0.0062, -0.0475, 0.0100, 2.4),
            (-0.142, 0.0020, -0.0520, 0.0088, 2.2),
            (-0.146, -0.0070, -0.0535, 0.0072, 2.2),
            (-0.1485, -0.0190, -0.0490, 0.0045, 2.0),
        ],
    )
    add_prong(
        grip,
        [
            (0.0, -0.018, -0.0515),
            (0.0, -0.032, -0.0520),
            (0.0, -0.043, -0.0495),
            (0.0, -0.051, -0.0450),
            (0.0, -0.0545, -0.0410),
        ],
        [0.0078, 0.0076, 0.0070, 0.0060, 0.0034],
        [0.0056, 0.0050, 0.0046, 0.0040, 0.0026],
    )
    add_prong(
        grip,
        [
            (0.0, -0.022, -0.0790),
            (0.0, -0.036, -0.0805),
            (0.0, -0.045, -0.0845),
            (0.0, -0.0500, -0.0900),
            (0.0, -0.0515, -0.0935),
        ],
        [0.0076, 0.0072, 0.0066, 0.0056, 0.0030],
        [0.0054, 0.0048, 0.0044, 0.0038, 0.0024],
    )
    build_hex_nut(steel, -0.139, -0.150, 0.0076)


def build_belgian(grip, steel):
    """The Belgian: a fuller, squarer pistol grip with a thumb stop on top, a broad front prong that hooks forward
    under the index finger, a wide second prong and a short square butt."""
    build_pistol_body(
        grip,
        [
            (PAD_BACK_Z, 0.0112, -0.0180, 0.0128, 3.4),
            (-0.033, 0.0110, -0.0225, 0.0132, 3.4),
            (-0.046, 0.0104, -0.0280, 0.0138, 3.6),
            (-0.064, 0.0100, -0.0310, 0.0142, 3.6),
            (-0.084, 0.0098, -0.0318, 0.0142, 3.6),
            (-0.102, 0.0095, -0.0330, 0.0138, 3.4),
            (-0.116, 0.0090, -0.0385, 0.0132, 3.2),
            (-0.126, 0.0080, -0.0430, 0.0124, 3.0),
            (-0.133, 0.0060, -0.0445, 0.0112, 3.0),
            (-0.137, 0.0000, -0.0430, 0.0090, 3.0),
            (-0.139, -0.0100, -0.0380, 0.0050, 2.4),
        ],
    )
    # The thumb stop: a low ridge across the top just behind the pad.
    grip.add_loft(
        [
            ((0.0, 0.0100, -0.032), 0.0104, 0.0020, 2.0),
            ((0.0, 0.0135, -0.037), 0.0110, 0.0032, 2.0),
            ((0.0, 0.0100, -0.042), 0.0104, 0.0020, 2.0),
        ],
        12,
        side_reference=(1.0, 0.0, 0.0),
    )
    add_prong(
        grip,
        [
            (0.0, -0.018, -0.0540),
            (0.0, -0.034, -0.0545),
            (0.0, -0.047, -0.0520),
            (0.0, -0.056, -0.0460),
            (0.0, -0.0600, -0.0400),
            (0.0, -0.0605, -0.0360),
        ],
        [0.0108, 0.0106, 0.0100, 0.0090, 0.0076, 0.0040],
        [0.0064, 0.0060, 0.0056, 0.0050, 0.0042, 0.0026],
    )
    add_prong(
        grip,
        [
            (0.0, -0.024, -0.0830),
            (0.0, -0.038, -0.0845),
            (0.0, -0.048, -0.0880),
            (0.0, -0.0540, -0.0935),
            (0.0, -0.0555, -0.0965),
        ],
        [0.0104, 0.0100, 0.0094, 0.0082, 0.0040],
        [0.0060, 0.0056, 0.0050, 0.0044, 0.0026],
    )
    build_hex_nut(steel, -0.131, -0.142, 0.0080)


def build_hilt(name, build_grip):
    clear_scene()
    bell = build_bell()
    pad = build_pad()
    grip_builder, steel_builder = MeshBuilder(), MeshBuilder()
    build_grip(grip_builder, steel_builder)
    build_socket(grip_builder, steel_builder)
    grip = grip_builder.build("Grip", smooth=True, smooth_angle_degrees=60.0)
    steel_parts = steel_builder.build(
        "SteelParts", smooth=True, smooth_angle_degrees=40.0
    )
    join([bell, steel_parts], "Steel")
    pad.name = "Pad"
    export_fbx(os.path.join(BLADE_MODELS_DIR, f"{name}.fbx"))


# ---------------------------------------------------------------------------------------------------------------- mask

# The mask's bowl: a boxy superellipsoid round the head, in the face frame (head centre at the origin).
MASK_HALF_WIDTH = 0.108
MASK_HALF_HEIGHT = 0.155
MASK_HALF_DEPTH = 0.128
MASK_CENTRE = (0.0, -0.018, 0.004)
MASK_SIDE_ANGLE = math.radians(98.0)
MASK_TOP_ANGLE = math.radians(56.0)
MASK_BOTTOM_ANGLE = math.radians(-52.0)
MASK_HORIZONTAL_POWER = 2.6
MASK_VERTICAL_POWER = 2.6


def mask_point(azimuth, elevation, inflate=0.0):
    """A point on the mask's bowl: azimuth 0 straight ahead, positive to the right; elevation positive up. Boxy both
    ways (superellipses), so the front is flat over the face and the chin and brow are broad, as on a real mask."""
    sin_a, cos_a = math.sin(azimuth), math.cos(azimuth)
    horizontal_exponent = 2.0 / MASK_HORIZONTAL_POWER
    horizontal_x = math.copysign(abs(sin_a) ** horizontal_exponent, sin_a)
    horizontal_z = math.copysign(abs(cos_a) ** horizontal_exponent, cos_a)
    sin_e, cos_e = math.sin(elevation), math.cos(elevation)
    vertical_exponent = 2.0 / MASK_VERTICAL_POWER
    vertical_y = math.copysign(abs(sin_e) ** vertical_exponent, sin_e)
    vertical_r = abs(cos_e) ** vertical_exponent
    return (
        MASK_CENTRE[0] + ((MASK_HALF_WIDTH + inflate) * horizontal_x * vertical_r),
        MASK_CENTRE[1] + ((MASK_HALF_HEIGHT + inflate) * vertical_y),
        MASK_CENTRE[2] + ((MASK_HALF_DEPTH + inflate) * horizontal_z * vertical_r),
    )


def build_mask_mesh():
    """The wire front, one surface with UVs that tile the grid texture (the material repeats it)."""
    columns, rows = 36, 22
    builder = MeshBuilder()
    grid = []
    for row in range(rows + 1):
        elevation = lerp(MASK_BOTTOM_ANGLE, MASK_TOP_ANGLE, row / rows)
        line = []
        for column in range(columns + 1):
            azimuth = lerp(-MASK_SIDE_ANGLE, MASK_SIDE_ANGLE, column / columns)
            line.append(
                builder.add_vertex(
                    mask_point(azimuth, elevation), uv=(column / columns, row / rows)
                )
            )
        grid.append(line)
    for row in range(rows):
        for column in range(columns):
            builder.faces.append(
                (
                    grid[row][column],
                    grid[row][column + 1],
                    grid[row + 1][column + 1],
                    grid[row + 1][column],
                )
            )
    obj = builder.build("MaskMesh", smooth=True, smooth_angle_degrees=89.0)
    return obj


def build_mask_trim(builder):
    """The padded trim round the bowl's edge: a tube along its boundary."""
    boundary = []
    steps_side, steps_vertical = 48, 28
    for i in range(steps_side):
        boundary.append(
            (lerp(-MASK_SIDE_ANGLE, MASK_SIDE_ANGLE, i / steps_side), MASK_TOP_ANGLE)
        )
    for i in range(steps_vertical):
        boundary.append(
            (
                MASK_SIDE_ANGLE,
                lerp(MASK_TOP_ANGLE, MASK_BOTTOM_ANGLE, i / steps_vertical),
            )
        )
    for i in range(steps_side):
        boundary.append(
            (lerp(MASK_SIDE_ANGLE, -MASK_SIDE_ANGLE, i / steps_side), MASK_BOTTOM_ANGLE)
        )
    for i in range(steps_vertical):
        boundary.append(
            (
                -MASK_SIDE_ANGLE,
                lerp(MASK_BOTTOM_ANGLE, MASK_TOP_ANGLE, i / steps_vertical),
            )
        )

    tube_segments, radius = 10, 0.0085
    rings = []
    count = len(boundary)
    for index, (azimuth, elevation) in enumerate(boundary):
        centre = mask_point(azimuth, elevation, inflate=0.002)
        previous = mask_point(*boundary[index - 1], inflate=0.002)
        following = mask_point(*boundary[(index + 1) % count], inflate=0.002)
        tangent = normalize(tuple(a - b for a, b in zip(following, previous)))
        outward = normalize(tuple(c - m for c, m in zip(centre, MASK_CENTRE)))
        side = normalize(add(outward, scale(tangent, -dot(outward, tangent))))
        up = cross(tangent, side)
        ring = []
        for segment in range(tube_segments):
            angle = 2 * math.pi * segment / tube_segments
            ring.append(
                builder.add_vertex(
                    add(
                        centre,
                        add(
                            scale(side, radius * 0.75 * math.cos(angle)),
                            scale(up, radius * math.sin(angle)),
                        ),
                    )
                )
            )
        rings.append(ring)
    for index in range(count):
        this_ring, next_ring = rings[index], rings[(index + 1) % count]
        for segment in range(tube_segments):
            following = (segment + 1) % tube_segments
            builder.faces.append(
                (
                    this_ring[segment],
                    this_ring[following],
                    next_ring[following],
                    next_ring[segment],
                )
            )


def build_mask_bib(builder):
    """The bib: a padded flap from the bowl's lower edge down over the throat, a little wider at the bottom."""
    columns, rows = 20, 6
    thickness = 0.008
    drop = 0.105
    front, back = [], []
    for row in range(rows + 1):
        t = row / rows
        front_line, back_line = [], []
        for column in range(columns + 1):
            azimuth = lerp(
                -MASK_SIDE_ANGLE * 0.9, MASK_SIDE_ANGLE * 0.9, column / columns
            )
            top = mask_point(azimuth, MASK_BOTTOM_ANGLE, inflate=0.002)
            # Hangs down, flares out a little and curves in under the chin at the top.
            outward = normalize((top[0], 0.0, top[2] - MASK_CENTRE[2]))
            spread = 0.035 * (t**1.3)
            point = add(
                top,
                add(
                    (0.0, -drop * t, 0.0),
                    scale(
                        outward,
                        spread - (0.010 * math.sin(math.pi * min(1.0, t * 1.4)) * 0.5),
                    ),
                ),
            )
            point = add(point, (0.0, 0.0, 0.012 * t * t))
            front_line.append(
                builder.add_vertex(add(point, scale(outward, thickness * 0.5)))
            )
            back_line.append(
                builder.add_vertex(add(point, scale(outward, -thickness * 0.5)))
            )
        front.append(front_line)
        back.append(back_line)
    for row in range(rows):
        for column in range(columns):
            builder.faces.append(
                (
                    front[row][column],
                    front[row][column + 1],
                    front[row + 1][column + 1],
                    front[row + 1][column],
                )
            )
            builder.faces.append(
                (
                    back[row][column],
                    back[row + 1][column],
                    back[row + 1][column + 1],
                    back[row][column + 1],
                )
            )
    for column in range(columns):
        builder.faces.append(
            (
                front[rows][column],
                front[rows][column + 1],
                back[rows][column + 1],
                back[rows][column],
            )
        )
    for row in range(rows):
        builder.faces.append(
            (front[row][0], front[row + 1][0], back[row + 1][0], back[row][0])
        )
        builder.faces.append(
            (
                front[row][columns],
                back[row][columns],
                back[row + 1][columns],
                front[row + 1][columns],
            )
        )


def build_mask_tongue(builder):
    """The back strap (the spring tongue) from the top of the bowl over the crown to the back of the head."""
    path = []
    for step in range(13):
        t = step / 12.0
        angle = lerp(math.radians(60.0), math.radians(222.0), t)
        # Over the head in the y-z plane, hugging an ellipse a little larger than the head sphere.
        path.append(
            (0.0, 0.122 * math.sin(angle) + 0.004, 0.150 * math.cos(angle) - 0.004)
        )
    sections = [
        (point, 0.020 if 0 < i < len(path) - 1 else 0.016, 0.0028, 4.0)
        for i, point in enumerate(path)
    ]
    builder.add_loft(sections, 12, side_reference=(1.0, 0.0, 0.0))


def build_mask():
    clear_scene()
    build_mask_mesh()
    fabric = MeshBuilder()
    build_mask_trim(fabric)
    build_mask_bib(fabric)
    build_mask_tongue(fabric)
    fabric.build("MaskFabric", smooth=True, smooth_angle_degrees=70.0)
    export_fbx(os.path.join(DUMMY_MODELS_DIR, "FencingMask.fbx"))


# ---------------------------------------------------------------------------------------------------------------- output


def export_fbx(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        mesh_smooth_type="OFF",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        colors_type="SRGB",
        path_mode="STRIP",
    )
    triangles = sum(
        sum(len(p.vertices) - 2 for p in obj.data.polygons)
        for obj in bpy.context.scene.objects
        if obj.type == "MESH"
    )
    print(
        f"Wrote {os.path.relpath(path, REPO_ROOT)}: {len(bpy.context.scene.objects)} objects, {triangles} triangles"
    )


PNG_SIGNATURE = bytes((0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))


def write_png(path, size, pixel):
    """Writes a size x size RGBA PNG (straight alpha) with pure Python; `pixel(x, y)` returns four bytes."""
    rows = []
    for y in range(size):
        row = bytearray([0])  # PNG filter type 0 for this scanline.
        for x in range(size):
            row += bytes(pixel(x, y))
        rows.append(bytes(row))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    png = PNG_SIGNATURE + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(b"".join(rows), 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(png)
    print(f"Wrote {os.path.relpath(path, REPO_ROOT)}")


def write_mask_grid_png(path, size=32, wire=3):
    """One cell of the mask's wire mesh, tiled by the material: near-black steel wire on the cell's edges, and a dark
    see-through hole, so the head behind reads dim as through a real mask."""

    def pixel(x, y):
        distance = min(x, size - 1 - x, y, size - 1 - y)
        return (46, 48, 52, 255) if distance < wire else (8, 8, 10, 205)

    write_png(path, size, pixel)


def write_hatch_png(path, size=64):
    """One tile of the Warning Zone's diagonal hatching: white stripes on a fainter ground (the material tints it),
    with soft stripe edges so it does not shimmer on the floor."""

    def pixel(x, y):
        phase = ((x + y) % size) / size
        stripe = 0.5 - abs(phase - 0.5)  # 0 at the stripe's centre line, 0.5 halfway between stripes.
        coverage = min(1.0, max(0.0, (0.2 - stripe) / 0.04 + 0.5))
        alpha = int(round(255 * (0.4 + (0.6 * coverage))))
        return (255, 255, 255, alpha)

    write_png(path, size, pixel)


def main():
    build_hilt("HiltFrench", build_french_grip)
    build_hilt("HiltVisconti", build_visconti)
    build_hilt("HiltBelgian", build_belgian)
    build_mask()
    write_mask_grid_png(os.path.join(DUMMY_TEXTURES_DIR, "MaskWireGrid.png"))
    write_hatch_png(os.path.join(STRIP_TEXTURES_DIR, "WarningZoneHatch.png"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
