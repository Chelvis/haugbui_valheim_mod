using Haugbui.Core.Creature;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class WorkBudgetTests
    {
        [Test]
        public void Banks_only_skipped_time_in_which_it_may_work()
        {
            var budget = new WorkBudget();
            budget.Observe(0, skipping: true, mayWork: false);
            budget.Observe(600, skipping: true, mayWork: false);
            Assert.That(budget.Seconds, Is.EqualTo(0), "afternoon skipped before night adds nothing without the Altar");
            budget.Observe(900, skipping: true, mayWork: true);
            Assert.That(budget.Seconds, Is.EqualTo(300));
        }

        [Test]
        public void Ends_with_the_skip_and_never_runs_backwards()
        {
            var budget = new WorkBudget();
            budget.Observe(0, true, true);
            budget.Observe(100, true, true);
            budget.Observe(50, true, true);
            Assert.That(budget.Seconds, Is.EqualTo(100));
            budget.Observe(60, false, true);
            Assert.That(budget.Seconds, Is.EqualTo(0));
        }

        [Test]
        public void Steps_cost_travel_plus_interaction()
        {
            var budget = new WorkBudget();
            budget.Observe(0, true, true);
            budget.Observe(30, true, true);
            double cost = WorkBudget.StepCost(20, 4);
            Assert.That(cost, Is.EqualTo(5 + WorkBudget.InteractionSeconds));
            Assert.That(budget.TrySpend(cost), Is.True);
            Assert.That(budget.Seconds, Is.EqualTo(30 - cost).Within(1e-9));
            Assert.That(budget.TrySpend(100), Is.False);
        }

        [Test]
        public void Bank_is_capped()
        {
            var budget = new WorkBudget { MaxSeconds = 50 };
            budget.Observe(0, true, true);
            budget.Observe(1000, true, true);
            Assert.That(budget.Seconds, Is.EqualTo(50));
        }
    }
}
