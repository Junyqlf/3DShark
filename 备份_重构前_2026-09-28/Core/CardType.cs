namespace SandKill.Core
{
    /// <summary>卡牌类型。工人=平民牌，其余为角色牌。</summary>
    public enum CardType
    {
        Worker = 0,   // 工人：维持存活，全部失去则出局
        Undead,       // 亡灵：免疫攻击，被抽到时返回手牌；驱魔师可处决
        Exorcist,     // 驱魔师：每回合可抽对手一张牌，是亡灵则处决，否则归还
        Police,       // 警察：每回合可扣押对手一张牌，沉默1回合后归还
        Priest,       // 牧师：被抽走进入弃牌堆时触发亡语，从弃牌堆拿一张角色牌回手
        Guard,        // 侍卫：被攻击时可代替被抽中的牌进入弃牌堆（挡刀）
        Witch,        // 女巫：毒药=额外攻击一次；解药=从弃牌堆拿一张角色牌回手（各限一次）
        Arsenal,      // 军火库：每回合可额外攻击一次
        Hero,         // 英雄（2代）：永不离手；被攻击/扣押/勒索打中时改为扣1点HP
        ArmedGuard,   // 带刀侍卫（2代）：主动技，1回合内己方侍卫和大臣获得亡语"攻击1次"
        Minister,     // 大臣（2代）：主动技"贿赂"，交一张牌给一名玩家，对方1回合内不能攻击你
        GrandMinister, // 军机大臣（2代）：主动技"勒索"，获得其他所有人手牌中某种类型的全部牌
        Warden,       // 守卫（2代）：每回合可藏起一张手牌，不能连藏同一张、不能藏自己
        Maid          // 侍女（2代）：无技能，免疫军火库的追加攻击
    }

    public static class CardTypeExtensions
    {
        public static string Name(this CardType t)
        {
            switch (t)
            {
                case CardType.Worker:   return "工人";
                case CardType.Undead:   return "亡灵";
                case CardType.Exorcist: return "驱魔师";
                case CardType.Police:   return "警察";
                case CardType.Priest:   return "牧师";
                case CardType.Guard:    return "侍卫";
                case CardType.Witch:    return "女巫";
                case CardType.Arsenal:  return "军火库";
                case CardType.Hero:         return "英雄";
                case CardType.ArmedGuard:   return "带刀侍卫";
                case CardType.Minister:     return "大臣";
                case CardType.GrandMinister: return "军机大臣";
                case CardType.Warden:       return "守卫";
                case CardType.Maid:         return "侍女";
                default:                return "?";
            }
        }

        /// <summary>是否为角色牌（非工人）。牧师亡语、女巫解药只能回收角色牌。</summary>
        public static bool IsRole(this CardType t) { return t != CardType.Worker; }
    }
}
