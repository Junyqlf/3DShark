using System.Collections.Generic;
using SandKill.Core;

namespace SandKill.Unity
{
    /// <summary>
    /// 对局会话公共接口（2026-10-21）：GameUI2 对 2 代（GameBootstrap2）与
    /// 3 代（GameBootstrap3）会话一视同仁——帮助页牌表、攻击后摇、再来一局、返回主菜单。
    /// </summary>
    public interface IGameSession
    {
        Dictionary<CardType, int> DeckSpec();
        float AttackBackswing { get; }
        void Restart();
        void BackToMenu();
    }
}
