namespace SandKill.Core
{
    /// <summary>一张卡牌实例。技能使用状态记录在卡上（同名多张卡各自独立）。</summary>
    public class Card
    {
        public int Id;
        public CardType Type;

        /// <summary>本回合技能是否已使用（驱魔师/警察/军火库：每回合每张一次）。
        /// 初代引擎用布尔值 + 回合结束统一重置；2代引擎改用回合戳（UsedSkillTurn），本字段仅供初代。</summary>
        public bool UsedSkillThisTurn;

        /// <summary>2代：上次使用技能的回合戳（引擎每回合自增）。等于当前戳 = 本回合已用。
        /// 回合戳机制保证"不在手牌里的牌错过重置"（被扣押/藏起/离手后归还）绝不可能发生。</summary>
        public int UsedSkillTurn = -1;

        /// <summary>女巫专用：毒药是否已用（整局一次）。</summary>
        public bool PoisonUsed;

        /// <summary>女巫专用：解药是否已用（整局一次）。</summary>
        public bool AntidoteUsed;

        /// <summary>军机大臣专用：勒索是否已用（整局一次，限定技）。</summary>
        public bool ExtortUsed;

        /// <summary>阵亡回合戳：进入弃牌堆时由引擎记录当前回合戳；-1=从未进过弃牌堆。
        /// 女巫解药只能救活 DiedStamp ≥ 施救者上一回合开始戳 的牌（上一回合内阵亡）。</summary>
        public int DiedStamp = -1;

        /// <summary>守卫（2代 2026-10-15 改版）：被施加"无法选取"状态时记录拥有者当时的 OwnTurnCount；-1=未在状态中。
        /// 状态持续到拥有者下个回合开始（StartTurn 统一清除）。期间牌留在手牌，但不参与一切针对手牌的判定
        /// （攻击抽牌/挡刀/驱魔/审判/扣押/人质/勒索/贿赂/合成材料）。</summary>
        public int GuardProtectTurn = -1;

        /// <summary>守卫专用（2026-10-15 用户裁定）：上次被守卫护住时的拥有者 OwnTurnCount——状态到期清除后此记忆仍保留，
        /// 用于"不能连续两个回合护同一张牌"（防连守与状态到期是本质区别：到期只解除无法选取，不解除守过的记忆）。-99=从未被护过。</summary>
        public int LastGuardedTurn = -99;

        /// <summary>青苹果专用：受伤倒数剩余回合（2 = 刚受伤；0/-1 = 未在倒数）。倒数只在拥有者的回合开始时走表。</summary>
        public int AppleTimer = -1;

        // ---- 合成牌状态（2026-10-06）----
        /// <summary>合成牌剩余生命（-1 = 非合成牌）。合成时=2；女祭司【祈祷】每次转化-1，耗尽则进入弃牌堆。</summary>
        public int SynthHP = -1;

        /// <summary>女祭司专用：【复活】是否已用（整局限一次）。</summary>
        public bool ReviveUsed;

        /// <summary>女祭司专用：【祈祷】是否已用（整局限一次）。</summary>
        public bool PrayerUsed;

        /// <summary>女祭司专用：【祈祷】到期回合计数（拥有者 OwnTurnCount ≥ 此值时结算；-1 = 未在祈祷）。</summary>
        public int PrayerDueTurn = -1;

        // ---- 恐怖分子（2代 2026-10-13 改版）：主动【安放炸弹】----
        /// <summary>炸弹是否已安放并倒数中。</summary>
        public bool BombActive;
        /// <summary>安放者座位。触发器绑定在玩家身上（到安放者的回合检查恐怖分子还活不活）；
        /// 恐怖分子被大臣拿去贿赂送人也不改目标——爆炸仍以安放者的敌我划线。</summary>
        public int BombPlanter = -1;
        /// <summary>爆炸回合：安放者 OwnTurnCount ≥ 此值时结算（安放时 = OwnTurnCount + 2）。</summary>
        public int BombDueTurn = -1;

        /// <summary>典狱长专用：上次扣押时的 OwnTurnCount（2回合冷却：OwnTurnCount - SeizeStamp ≥ 2 才可再扣押；-1=从未扣押）。</summary>
        public int SeizeStamp = -1;

        /// <summary>封印者座位（2026-10-19 美人计魅惑解封需要区分"谁封的"：只有魅惑主目标封的才会被解除；-1=无封印）。</summary>
        public int SealedBy = -1;

        /// <summary>死亡出厂重置（2026-10-22）：牌进入弃牌堆时由引擎（ToDiscard）调用，下一条命从全白状态开始——
        /// 女巫双药、军机大臣勒索、女祭司复活/祈祷、青苹果倒数、恐怖分子炸弹、守卫保护、封印、扣押冷却全部清零。
        /// 回合戳类字段（UsedSkillTurn）不在此清——它们靠判定时比较自然过期。
        /// LastGuardedTurn 一并清零（-99）：新命不应背着旧命的"防连守"记忆。
        /// DiedStamp 不在此清——它由引擎在本次死亡时重新写入，且是解药救活窗口的依据。</summary>
        public void ResetForNewLife()
        {
            // 限定技布尔家族
            PoisonUsed = AntidoteUsed = ExtortUsed = false;
            ReviveUsed = PrayerUsed = false;

            // 定时器家族
            AppleTimer = -1;
            PrayerDueTurn = -1;

            // 恐怖分子炸弹（文档已定稿：恐怖分子阵亡则炸弹失效）
            BombActive = false; BombPlanter = -1; BombDueTurn = -1;

            // 防御性清理（正常流程应已在别处清过，这里兜底）
            GuardProtectTurn = -1;
            LastGuardedTurn = -99;
            SealedBy = -1;
            SeizeStamp = -1;
        }

        public Card(int id, CardType type)
        {
            Id = id;
            Type = type;
        }

        public override string ToString() { return Type.Name() + "#" + Id; }
    }
}
