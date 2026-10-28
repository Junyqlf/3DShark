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
        Hide,          // 守卫：藏起一张手牌
        // ---- 合成系统（2026-10-06）----
        Craft,         // 合成：消耗1行动点+配方3张牌，获得一张2点生命的合成牌，奖励抽1张牌
        SynthRevive,   // 女祭司·复活（限定技）：从弃牌堆复活1张角色牌
        SynthPrayer,   // 女祭司·祈祷（限定技）：2回合后自身1点生命转化为英雄1点生命
        SynthHostage,  // 典狱长·扣押（每回合限1次）：对方1张牌来作人质并封印，1回合后归还解封
        SynthJudge,    // 审判官·审判：随机揭示对手1张牌，亡灵/女巫（异端）则处决
        BombPlant,     // 恐怖分子·安放炸弹（2026-10-13 改版）：2回合后爆炸，对每个敌手各1次无来源随机攻击（等同普攻，不记仇），恐怖分子自己同归于尽
        // ---- 新英雄技（2026-10-14）----
        ChivalryStrike,// （已废 2026-10-19：骑士精神重做=冷酷追猎，改为回合末自动处决，无此行动类型；枚举成员保留以兼容旧UI引用）
        Deathblow,     // 致命一击·主动技：对1名HP=1的玩家发动1次攻击，每个目标限1次
        // ---- 开局调度券（2026-10-16 用户设计：学炉石开局换牌，1券换1张，目前仅开放开局调度一种用途）----
        Mulligan,      // 开局调度：把1张手牌放回牌堆底，从牌堆顶补1张（消耗1张调度券；换掉哪张不公开）
        EndMulligan,   // 结束自己的开局调度（提前收工/无券可换）
        // ---- 3代·法术迸发（2026-10-20）----
        Learn,         // 学习法术：消耗1技能点+1行动点，点亮一种法术的释放权限（TargetType=法术类型），下个自己回合开始才生效
        CastSpell      // 释放法术：SourceCardId=法术牌，TargetType=法术类型，TargetSeat/SecondCardId=目标（按法术而定）
    }

    /// <summary>一次玩家行动。TargetSeat=目标座位（需要目标的行动），SourceCardId=发动技能的角色卡。</summary>
    public class GameAction
    {
        public ActionKind Kind;
        public int TargetSeat = -1;
        public int SourceCardId = -1;
        public CardType TargetType;        // Extort：勒索的牌型 / Learn：要学的法术类型
        public int SecondCardId = -1;      // Bribe：交给对方的牌（SourceCardId=大臣本身）/ 3代Clarity/点燃：目标牌
        public int ThirdCardId = -1;       // 3代清晰术：第二个对手的被标记牌（拥有者必须与第一个不同）

        public override string ToString()
        {
            return Kind + " target:" + TargetSeat + " src:" + SourceCardId;
        }
    }
}
