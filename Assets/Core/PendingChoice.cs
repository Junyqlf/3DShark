using System.Collections.Generic;

namespace SandKill.Core
{
    public enum ChoiceKind
    {
        GuardIntercept,   // 侍卫挡刀抉择：被攻击方决定是否用侍卫代替
        PickFromDiscard,  // 从弃牌堆选牌：牧师亡语 / 女巫解药
        DeathrattleAttack, // 带刀侍卫亡语（2代）：自由选择攻击目标
        CometCounter,     // 3代·法术反制：有人释放法术时，持彗星的真人玩家抉择是否消耗1张彗星抵消
        ClarityPick,      // 3代·清晰术（2026-10-21 重做）：看牌库顶3张，真人选1张加入手牌（Options=那3张）
        IgnitePick,       // 3代·点燃（2026-10-21② 用户强化）：查看目标对手2张手牌，真人选1张点燃（Options=那2张；AttackerSeat=目标座位；Reason=目标名字）
        // AlchemyPick 已随 2026-10-22 中午炼金术重做（直接释放，不再从牌库回收）移除
    }

    /// <summary>
    /// 引擎暂停等待真人玩家抉择的请求。
    /// 当真人需要做出选择时，引擎设置 Pending 并暂停，UI 展示后调用 SubmitGuardChoice / SubmitDiscardPick 继续。
    /// AI 的抉择在引擎内部同步完成，不会产生 Pending。
    /// </summary>
    public class PendingChoice
    {
        public ChoiceKind Kind;
        public int Seat;                  // 由谁抉择
        public Card DrawnCard;            // GuardIntercept：被抽中的牌
        public int AttackerSeat = -1;     // GuardIntercept：攻击者
        public ActionKind AttackKind;     // GuardIntercept（2代）：攻击类型（侍女只免疫军火库）
        public List<Card> Interceptors;   // GuardIntercept（2代）：可用来挡刀的牌（侍卫/女武神/圣骑士；牺牲英雄还有牧师）——UI逐张展示
        public List<Card> Options;        // PickFromDiscard：可选的牌
        public string Reason;             // PickFromDiscard：来源（"牧师亡语"/"女巫·解药"）
    }
}
