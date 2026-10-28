using System.Collections.Generic;

namespace SandKill.Core
{
    /// <summary>
    /// 玩家决策接口。AI 实现它做同步决策；真人不需要实现决策方法（UI 直接驱动引擎）。
    /// </summary>
    public interface IPlayerBrain
    {
        bool IsHuman { get; }

        /// <summary>回合行动阶段：从合法行动中选择一个。</summary>
        GameAction ChooseAction(GameEngine g, int seat, List<GameAction> legal);

        /// <summary>侍卫挡刀抉择：自己的 drawn 牌被攻击抽中，是否用一张侍卫代替它进弃牌堆。</summary>
        bool ChooseGuardIntercept(GameEngine g, int seat, Card drawn);

        /// <summary>从弃牌堆选项中选一张（牧师亡语 / 女巫解药）。</summary>
        Card ChooseFromDiscard(GameEngine g, int seat, List<Card> options, string reason);
    }
}
