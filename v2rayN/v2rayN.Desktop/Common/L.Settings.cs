namespace v2rayN.Desktop;

// Settings rows that used v2rayN's wording: plain titles, and a hint where the name alone says little.
public static partial class L
{
    public static string OptBindInterface => T("Network adapter", "Сетевой адаптер");
    public static string OptBindInterfaceHint => T("Which adapter connections go out through when the computer has several. AUTO picks a working one, wired first; empty leaves it to the system. On Linux and macOS it works only in VPN mode.",
        "Через какой адаптер выходить в сеть, если их несколько. AUTO — выбрать рабочий, проводной в первую очередь, пусто — решит система. В Linux и macOS действует только в режиме VPN.");
}
