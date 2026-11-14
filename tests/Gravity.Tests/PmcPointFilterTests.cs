using Gravity.Shared;
using Xunit;

namespace Gravity.Tests;

/// <summary>
/// 1.0.6 로컬 판(세관)에서 찾은 버그: 게임은 보스 웨이브 PMC를 EPlayerSide.Savage로 만든다(BotBossSpawn.cs:278).
/// 플레이어 시작 지점은 Sides=Pmc라 진영 검사에 늘 걸려, "시작 PMC는 플레이어 시작 지점만"이면 시작 PMC가 하나도 안 나왔다.
/// </summary>
public class PmcPointFilterTests
{
    [Fact]
    public void Player_start_point_is_usable_even_when_game_side_check_fails()
        => Assert.True(PmcPointFilter.Usable(timeOk: true, sideOk: false, playerPoint: true, onlyPlayer: true));

    [Fact]
    public void Bot_pmc_point_still_needs_game_side_check()
    {
        Assert.False(PmcPointFilter.Usable(timeOk: true, sideOk: false, playerPoint: false, onlyPlayer: false));
        Assert.True(PmcPointFilter.Usable(timeOk: true, sideOk: true, playerPoint: false, onlyPlayer: false));
    }

    [Fact]
    public void Cooldown_still_blocks_player_points()
        => Assert.False(PmcPointFilter.Usable(timeOk: false, sideOk: false, playerPoint: true, onlyPlayer: true));

    [Fact]
    public void Only_player_mode_skips_bot_points()
        => Assert.False(PmcPointFilter.Usable(timeOk: true, sideOk: true, playerPoint: false, onlyPlayer: true));
}
