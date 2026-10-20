using System.Runtime.CompilerServices;

// The opponent's blade (PER-81) is drawn by the same visual as the Fencer's own.
[assembly: InternalsVisibleTo("VrFencing.Partner.Unity")]

// Edit-mode tests check the hilt models (PER-41); the editor renders them to images (VR Fencing > Render model snapshots).
[assembly: InternalsVisibleTo("VrFencing.Tests.EditMode")]
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
