using System.Runtime.CompilerServices;

// Edit-mode tests build the panels and check text size and layout without a headset; the editor renders them to
// images (VR Fencing > Render menu snapshots) to check the layout on a desktop.
[assembly: InternalsVisibleTo("VrFencing.Tests.EditMode")]
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
