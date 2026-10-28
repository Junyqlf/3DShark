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
        public int LastTurnStamp;    // 我上一个回合开始时的回合戳（女巫解药"上一回合阵亡"判定窗口）
        public int LastBribeTurn;    // 2代：本座位上次贿赂时的 OwnTurnCount（2026-10-14④ 每回合限贿赂1次，防全场停战瘫痪）
        public int HeroId = -1;      // 2代：抽到的英雄（-1=初代无英雄）
        public string HeroName;      // 2代：英雄名称

        // ---- 合成奖励·方案C护盾（2026-10-13）：Charges=剩余吸收次数，ExpireOwnTurn=有效至自己的第N回合开始 ----
        public int ShieldCharges;
        public int ShieldExpireOwnTurn;

        // ---- 英雄升级（2026-10-21 用户裁定）：消灭6张升2级、8张升3级，每局最多升2次 ----
        public int EXP;                // 已消灭计数（封顶 GameEngine2.XP_LEVEL_3=8）
        public int HeroLevel;          // 已升级次数：0=初始(1级) / 1=2级 / 2=3级（升满）
        public int LevelTurn = -1;     // 最近一次升级时自己的 OwnTurnCount（攻城锤额外普攻窗口以此为锚）

        // ---- 3代·法术迸发（2026-10-20）：升2级奖励=1技能点（替代2代护盾，仅3代引擎发放）；升3级=技能点+回1HP（2026-10-21：仅3代；2代升3级与升2级相同=护盾）----
        public int SkillPoints;        // 技能点：learn 行动消耗1点，点亮一种法术的释放权限

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
