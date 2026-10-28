using System.Collections.Generic;

namespace SandKill.Core
{
    /// <summary>
    /// 2代玩家大脑接口。真人由UI驱动（方法不会被调用），AI由AIBrain2实现。
    /// 与初代 IPlayerBrain 独立（参数类型是 GameEngine2）。
    /// </summary>
    public interface IPlayerBrain2
    {
        bool IsHuman { get; }
        /// <summary>轮到自己时，从合法行动中选一个。</summary>
        GameAction ChooseAction(GameEngine2 g, int seat, List<GameAction> legal);
        /// <summary>被攻击且手上有侍卫：是否挡刀。</summary>
        bool ChooseGuardIntercept(GameEngine2 g, int seat, Card drawn, int attackerSeat);
        /// <summary>牧师亡语/女巫解药：从弃牌堆选项里挑一张。</summary>
        int ChooseFromDiscard(GameEngine2 g, int seat, List<Card> options);
        /// <summary>带刀侍卫亡语：自由选择攻击目标（返回目标座位）。</summary>
        int ChooseDeathrattleTarget(GameEngine2 g, int seat, List<int> candidates);
    }
}
