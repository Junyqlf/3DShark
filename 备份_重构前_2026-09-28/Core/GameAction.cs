namespace SandKill.Core
{
    public enum ActionKind
    {
        Attack,        // 基础攻击（每回合一次）
        ArsenalAttack, // 军火库追加攻击
        PoisonAttack,  // 女巫毒药追加攻击
        Exorcise,      // 驱魔师抽牌判定
        Seize,         // 警察扣押（沉默）
        Antidote,      // 女巫解药回收
        EndTurn,       // 结束回合
        // ---- 2代新增 ----
        Bribe,         // 大臣·贿赂：交一张牌给一名玩家，对方1回合内不能攻击你
        Extort,        // 军机大臣·勒索：获得其他所有人手牌中某种类型的全部牌
        ArmGuard,      // 带刀侍卫：1回合内己方侍卫和大臣获得亡语"攻击1次"
        HeroPower,     // 英雄主动技（2代：police station / save，整局限1次）
        Hide           // 守卫：藏起一张手牌
    }

    /// <summary>一次玩家行动。TargetSeat=目标座位（需要目标的行动），SourceCardId=发动技能的角色卡。</summary>
    public class GameAction
    {
        public ActionKind Kind;
        public int TargetSeat = -1;
        public int SourceCardId = -1;
        public CardType TargetType;        // Extort：勒索的牌型
        public int SecondCardId = -1;      // Bribe：交给对方的牌（SourceCardId=大臣本身）

        public override string ToString()
        {
            return Kind + " target:" + TargetSeat + " src:" + SourceCardId;
        }
    }
}
