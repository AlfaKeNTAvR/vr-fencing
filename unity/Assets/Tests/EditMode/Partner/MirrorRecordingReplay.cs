// Plays a real Fencer recording (Fixtures/mirror-me-20261022.fencer: every other frame of Nikita's
// fencer-20261022-164920, 22 s, right-handed, 1.65 m) through the whole partner path as "Mirror me" does (PER-140):
// the sender's body, footwork and hand messages, a 0.1 s loopback and the receiving OpponentLink. What comes out is
// the opponent as a partner's headset would draw it (PER-142).
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using VrFencing.Dummy;
using VrFencing.Partner;
using VrFencing.Session;
using VrFencing.Settings;
using VrFencing.Tracking;
using BodySample = VrFencing.Partner.BodySample;

namespace VrFencing.Tests.Partner
{
    internal sealed class MirrorRecordingReplay
    {
        public const string FixtureFileName = "mirror-me-20261022.fencer";

        private static readonly StripExtent MyStrip = new StripExtent(8.0f, -1.0f);

        private MirrorRecordingReplay(FencerRecordingHeader header, List<Frame> frames)
        {
            Header = header;
            Frames = frames;
        }

        public FencerRecordingHeader Header { get; }

        public float HeightMeters => Header.FencerHeightMeters;

        public Hand Hand => Header.Hand;

        /// <summary>Every frame that had a body, with what was sent and what the receiver draws for it.</summary>
        public IReadOnlyList<Frame> Frames { get; }

        /// <summary>The recording replayed through the partner path; frames before the receiver draws anything are skipped.</summary>
        public static MirrorRecordingReplay Run()
        {
            var recording = FencerRecordingFile.Read(GetFixturePath());
            var header = recording.Header;
            var link = new OpponentLink();
            link.SetMyStrip(MyStrip);
            var loopback = new MirrorLoopback(MirrorDelay.DefaultSeconds);
            var footwork = new FootworkLookAhead();
            var handMotion = new HandMotion();
            var bodySchedule = new SendSchedule(30.0);
            var frames = new List<Frame>();
            var start = recording.Frames[0].TimeSeconds;
            var isInfoSent = false;
            foreach (var recorded in recording.Frames)
            {
                var now = recorded.TimeSeconds - start;
                var body = FullBodyJoints.Read(recorded.Joints, recorded.Head?.Rotation, header.Hand, header.FencerHeightMeters, recorded.BodyFidelity);
                footwork.Update(now, recorded.Head?.Position, body.Get(SkeletonJoint.HipCenter), header.FencerHeightMeters, header.Hand);
                if (!isInfoSent)
                {
                    loopback.Send(PartnerMessages.EncodeInfo(new FencerInfo(MyStrip, header.FencerHeightMeters, header.Hand)), now);
                    isInfoSent = true;
                }

                if (recorded.SwordController.HasValue)
                {
                    handMotion.Add(now, recorded.SwordController.Value);
                    loopback.Send(PartnerMessages.EncodeHand(new HandSample(
                        now,
                        recorded.SwordController.Value,
                        handMotion.Velocity,
                        handMotion.AngularVelocity,
                        header.Hand,
                        BladeMount.French,
                        new GripAngles(0.0f, 0.0f),
                        0.0f,
                        BladeFlexSoftness.Normal)), now);
                }

                if (bodySchedule.TakeIfDue(now))
                {
                    loopback.Send(PartnerMessages.EncodeBody(new BodySample(now, body.WithFootwork(footwork.State))), now);
                }

                foreach (var message in loopback.TakeDue(now))
                {
                    if (!link.Receive(message, now))
                    {
                        throw new InvalidDataException("A partner message was refused.");
                    }
                }

                var view = link.Sample(now);
                if (view?.Body != null && recorded.BodyFidelity.HasValue)
                {
                    frames.Add(new Frame(now, recorded, body, footwork.State, view));
                }
            }

            return new MirrorRecordingReplay(header, frames);
        }

        /// <summary>The checked-in clip, next to this source file in the Unity project and in the dotnet Core tests alike.</summary>
        public static string GetFixturePath([CallerFilePath] string sourcePath = "")
        {
            var besideSource = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sourcePath)), "Fixtures", FixtureFileName);
            var inProject = Path.GetFullPath(Path.Combine("Assets", "Tests", "EditMode", "Partner", "Fixtures", FixtureFileName));
            return File.Exists(besideSource) || !File.Exists(inProject) ? besideSource : inProject;
        }

        /// <summary>The bout's half turn about the vertical: the receiver's frame turned back to the sender's facing.</summary>
        public static Quaternion HalfTurn => Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI);

        public sealed class Frame
        {
            public Frame(double timeSeconds, FencerFrame recorded, FencerBody sent, FootworkState sentFootwork, OpponentView view)
            {
                TimeSeconds = timeSeconds;
                Recorded = recorded;
                Sent = sent;
                SentFootwork = sentFootwork;
                View = view;
            }

            /// <summary>Seconds since the recording's first frame (the sender's and the receiver's clock here).</summary>
            public double TimeSeconds { get; }

            public FencerFrame Recorded { get; }

            /// <summary>The body as read on the sender, in its own Strip frame (facing +z), before the footwork.</summary>
            public FencerBody Sent { get; }

            public FootworkState SentFootwork { get; }

            /// <summary>The opponent as drawn on the receiver, in its Strip frame (facing -z), 0.1 s plus the buffer late.</summary>
            public OpponentView View { get; }
        }
    }
}
