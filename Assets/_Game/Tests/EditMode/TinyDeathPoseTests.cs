using Emberfall.Editor.Review;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class TinyDeathPoseTests
    {
        [Test] public void LocalPoseEnvelope_PreservesInitialFallAndUsesBoundedContinuousAdjustment()
        {
            Assert.That(TinyDeathPoseRefinement.RefineWeight(0,.5f),Is.Zero);
            Assert.That(TinyDeathPoseRefinement.RefineWeight(.125f,.5f),Is.Zero);
            Assert.That(TinyDeathPoseRefinement.RefineWeight(.4f,.5f),Is.EqualTo(1));
            float prior=0;
            for(int i=0;i<=120;i++)
            {
                float value=TinyDeathPoseRefinement.RefineWeight(i*.5f/120,.5f);
                Assert.That(value,Is.InRange(prior,1));prior=value;
            }
        }
        [Test] public void LocalPoseEnvelope_RejectsInvalidDuration()
        {Assert.Throws<System.ArgumentOutOfRangeException>(()=>TinyDeathPoseRefinement.RefineWeight(0,0));}
    }
}
