using System.Collections.Generic;
using System.Linq;

namespace SandKill.Core
{
    /// <summary>玩家状态。</summary>
    public class PlayerState
    {
        public int Seat;                 // 座位号 0..n-1
        public string Name;
        public bool IsHuman;             // 是否真人（真人由UI驱动，AI由AIBrain驱动）
        public bool Alive = true;
        public List<Card> Hand = new List<Card>();

        // ---- 2代英雄系统（初代引擎不使用，保持默认0）----
        public int HP;               // 英雄生命值，归零出局
        public int MaxHP;
        public int MP;               // 法力值，回合开始回满
        public int MaxMP;
        public int OwnTurnCount;     // 自己经历过的回合数（天赋回血/免费抽牌的"每2回合"以此为节拍）
        public int HeroId = -1;      // 2代：抽到的英雄（-1=初代无英雄）
        public string HeroName;      // 2代：英雄名称

        public int WorkerCount
        {
            get { return Hand.Count(c => c.Type == CardType.Worker); }
        }

        public Card FirstOf(CardType t)
        {
            return Hand.FirstOrDefault(c => c.Type == t);
        }

        public override string ToString()
        {
            return Name + (Alive ? "" : "(出局)") + " 手牌:" + Hand.Count + " 工人:" + WorkerCount;
        }
    }
}
