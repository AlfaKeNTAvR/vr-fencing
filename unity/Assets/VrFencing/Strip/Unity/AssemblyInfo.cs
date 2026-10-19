using System.Runtime.CompilerServices;

// Edit-mode tests check the markings' layering (lines solid and above the Warning Zones) and the status text font;
// the editor renders the status text to an image (VR Fencing > Render menu snapshots).
[assembly: InternalsVisibleTo("VrFencing.Tests.EditMode")]
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
