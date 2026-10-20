using System.Runtime.CompilerServices;

// The opponent's rigged body (PER-81) is the Dummy's rig driven by the partner's tracked body.
[assembly: InternalsVisibleTo("VrFencing.Partner.Unity")]
[assembly: InternalsVisibleTo("VrFencing.Tests.EditMode")]
// The editor renders the Dummy and its mask to images (VR Fencing > Render model snapshots, PER-41).
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
