using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class CombatAudioVoiceBudgetTests
    {
        [Test] public void SoloAndEmptyBudgets_PreserveNominalGain()
        {
            foreach(CombatAudioImportance kind in System.Enum.GetValues(typeof(CombatAudioImportance)))
                foreach(int count in new[]{0,1})Assert.That(CombatAudioVoiceBudget.Gain(kind,count),Is.EqualTo(1));
        }
        [Test] public void ConcurrentGain_IsPositiveMonotonic_AndCriticalIsPreservedMore()
        {
            float prior=1;
            foreach(int count in new[]{2,6,32,128})
            {
                float strike=CombatAudioVoiceBudget.Gain(CombatAudioImportance.Strike,count);
                float critical=CombatAudioVoiceBudget.Gain(CombatAudioImportance.Critical,count);
                float confirmation=CombatAudioVoiceBudget.Gain(CombatAudioImportance.WorldConfirmation,count);
                Assert.That(strike,Is.GreaterThan(0).And.LessThan(prior));
                Assert.That(critical,Is.GreaterThan(strike).And.LessThanOrEqualTo(1));
                Assert.That(confirmation,Is.GreaterThan(0).And.LessThan(strike));prior=strike;
            }
        }
    }
}
