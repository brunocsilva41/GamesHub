// The page may ask to uninstall a game, but it only ever runs the uninstaller the user was shown (getGameInfo)
// and, for registry uninstallers (arbitrary programs), only after a native confirmation.
using System;
using System.Collections.Concurrent;
using System.Text;

namespace GamesHub
{
    /// <summary>The uninstaller last offered for each game (what the details page showed), so uninstallGame
    /// runs exactly that entry instead of searching again. Thread-safe; hands out copies.</summary>
    public sealed class UninstallOffers
    {
        private readonly ConcurrentDictionary<string, UninstallInfo> offers =
            new ConcurrentDictionary<string, UninstallInfo>(StringComparer.Ordinal);

        public void Remember(string gameId, UninstallInfo info)
        {
            if (string.IsNullOrEmpty(gameId)) return;
            if (info == null || info.Method == "none") offers.TryRemove(gameId, out _);
            else offers[gameId] = Copy(info);
        }

        /// <summary>Copy of the offer shown for the game, or null.</summary>
        public UninstallInfo Get(string gameId)
            => !string.IsNullOrEmpty(gameId) && offers.TryGetValue(gameId, out UninstallInfo i) ? Copy(i) : null;

        public void Forget(string gameId)
        {
            if (!string.IsNullOrEmpty(gameId)) offers.TryRemove(gameId, out _);
        }

        private static UninstallInfo Copy(UninstallInfo i)
            => new UninstallInfo { Method = i.Method ?? "none", Command = i.Command ?? "", DisplayName = i.DisplayName ?? "" };
    }

    /// <summary>Decision logic for uninstallGame; the native prompt and the runner are injected (testable).</summary>
    public static class UninstallGate
    {
        /// <summary>Registry uninstallers are programs chosen by matching: they need the user's native confirmation.
        /// Steam and Epic only open their launcher, which asks for confirmation itself.</summary>
        public static bool NeedsNativeConfirmation(UninstallInfo offer) => offer != null && offer.Method == "registry";

        /// <summary>Null when <paramref name="offer"/> may run now (asking <paramref name="confirm"/> when needed);
        /// otherwise the failure to report. No prompt available counts as "declined".</summary>
        public static OpResult Check(string gameName, UninstallInfo offer, Func<UninstallInfo, bool> confirm)
        {
            string name = string.IsNullOrEmpty(gameName) ? "o jogo" : gameName;
            if (offer == null)
                return OpResult.Fail("Abra os detalhes de " + name + " novamente para desinstalar.");
            if (offer.Method == "none" || string.IsNullOrWhiteSpace(offer.Command))
                return OpResult.Fail("Não encontrei um desinstalador para " + name + ". Use Configurações do Windows › Aplicativos.");
            if (NeedsNativeConfirmation(offer) && (confirm == null || !confirm(offer)))
                return OpResult.Fail("A desinstalação foi cancelada.");
            return null;
        }

        public static OpResult Run(string gameName, UninstallInfo offer, Func<UninstallInfo, bool> confirm, Func<UninstallInfo, OpResult> run)
            => Check(gameName, offer, confirm) ?? run(offer);

        /// <summary>pt-BR body of the native prompt: entry name plus the exact program and arguments that will run.</summary>
        public static string Describe(UninstallInfo offer, ParsedCommand command)
        {
            var sb = new StringBuilder();
            sb.Append("O GamesHub vai abrir o desinstalador de \"").Append(Log.EscapeControl(offer?.DisplayName ?? "")).Append("\".\n");
            sb.Append("\nPrograma: ").Append(Visible(command?.File ?? ""));
            sb.Append("\nArgumentos: ").Append(string.IsNullOrWhiteSpace(command?.Args) ? "(nenhum)" : Visible(command.Args));
            sb.Append("\n\nContinue só se este for mesmo o desinstalador do jogo.");
            return sb.ToString();
        }

        /// <summary>Control characters escaped and long runs of blanks shortened (nothing can hide off-screen).</summary>
        private static string Visible(string s)
            => System.Text.RegularExpressions.Regex.Replace(Log.EscapeControl(s ?? ""), " {4,}", m => " [" + m.Length + " espaços] ");
    }
}
