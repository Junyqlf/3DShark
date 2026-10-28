namespace SandKill.Core
{
    /// <summary>一张卡牌实例。技能使用状态记录在卡上（同名多张卡各自独立）。</summary>
    public class Card
    {
        public int Id;
        public CardType Type;

        /// <summary>本回合技能是否已使用（驱魔师/警察/军火库：每回合每张一次）。</summary>
        public bool UsedSkillThisTurn;

        /// <summary>女巫专用：毒药是否已用（整局一次）。</summary>
        public bool PoisonUsed;

        /// <summary>女巫专用：解药是否已用（整局一次）。</summary>
        public bool AntidoteUsed;
        public int LastHiddenTurn = -99; // 守卫（2代）：上次被藏起时的回合计数，防连藏

        public Card(int id, CardType type)
        {
            Id = id;
            Type = type;
        }

        public override string ToString() { return Type.Name() + "#" + Id; }
    }
}
