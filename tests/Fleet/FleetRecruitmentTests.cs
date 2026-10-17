using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet
{
    public class FleetRecruitmentTests
    {
        [Fact]
        public void The_fleet_is_offered_for_customer_service_once_the_player_owns_a_food_vehicle()
        {
            var agency = new[] { "ba:skill_cleaning", "ba:skill_customerservice" };
            Assert.Equal(new[] { FleetRecruitment.Skill }, FleetRecruitment.Skills(1, agency));
            Assert.Empty(FleetRecruitment.Skills(0, agency));
            Assert.Empty(FleetRecruitment.Skills(2, new[] { "ba:skill_cleaning" }));
            Assert.Empty(FleetRecruitment.Skills(2, null));
        }

        [Theory]
        [InlineData(30000, 3, 0, 30000)]
        [InlineData(30000, 3, 1, 20000)]
        [InlineData(30000, 3, 3, 0)]
        [InlineData(10000, 3, 1, 6666)]
        [InlineData(10000, 3, 5, 0)]
        [InlineData(0, 3, 0, 0)]
        [InlineData(10000, 0, 0, 0)]
        public void An_uninstall_refunds_the_candidates_not_found_yet(long price, int candidates, int found, long refund)
        {
            Assert.Equal(refund, FleetRecruitment.RefundCents(price, candidates, found));
        }
    }
}
