namespace v2rayN.Desktop;

// Sign-in with a provider by code (ServiceLib/V2pn/V2pnPartnerLogin.cs): six digits and a QR code, confirmed in the
// provider's bot; V2pn then adds the person's subscription itself.
public static partial class L
{
    // Where it starts: the walkthrough and the add menu.
    public static string ObProviderLogin => T("I have a subscription from a provider", "У меня подписка от провайдера");
    public static string ObProviderLoginHint => T("Confirm in the provider's Telegram bot, and V2pn adds it itself.", "Подтвердите вход в Telegram-боте провайдера, и V2pn добавит её сам.");
    public static string MenuProviderLogin => T("Sign in with a provider", "Войти через провайдера");
    public static string MenuProviderLoginDesc => T("A code to confirm in the provider's bot", "Код, который вы подтвердите в боте провайдера");

    // Several providers: which one.
    public static string LoginPickTitle => T("Which provider is yours?", "Какой у вас провайдер?");
    public static string LoginPickLead => T("V2pn signs in with the one your subscription is from.", "V2pn войдёт через того, у кого ваша подписка.");
    public static string LoginNoProviders => T("No provider offers sign-in by code yet.", "Пока ни один провайдер не предлагает вход по коду.");
    public static string LoginNoList => T("Could not get the list of providers. Check the connection and try again.", "Не получилось загрузить список провайдеров. Проверьте интернет и попробуйте ещё раз.");

    // The code.
    public static string LoginTitle => T("Sign in with {0}", "Вход через {0}");
    public static string LoginFrom => T("Sign-in by code", "Вход по коду");
    public static string LoginGetting => T("Getting a code from {0}…", "Получаю код у {0}…");
    public static string LoginScan => T("Scan the QR code with your phone, or open the bot on this computer.", "Наведите камеру телефона на QR-код или откройте бота на этом компьютере.");
    public static string LoginTypeCode => T("Send these six digits to {0}'s bot.", "Отправьте эти шесть цифр боту {0}.");
    public static string LoginCodeName => T("Code: {0}", "Код: {0}");
    public static string LoginQrName => T("QR code of {0}'s bot", "QR-код бота {0}");
    public static string LoginOpenBot => T("Open the bot", "Открыть бота");
    public static string LoginConfirmIn => T("Confirm the sign-in in {0}'s bot. The code is good for {1} min.", "Подтвердите вход в боте {0}. Код действует {1} мин.");
    public static string LoginConfirmInSeconds => T("Confirm the sign-in in {0}'s bot. The code is good for {1} s.", "Подтвердите вход в боте {0}. Код действует {1} с.");
    public static string LoginWaiting => T("Waiting for your confirmation…", "Жду подтверждения…");
    public static string LoginSilent => T("{0} is not answering; trying again…", "{0} не отвечает, пробую ещё…");
    public static string LoginAdding => T("Confirmed. Adding the subscription…", "Вход подтверждён. Добавляю подписку…");
    public static string LoginNewCode => T("New code", "Новый код");
    public static string LoginCancel => T("Cancel", "Отмена");
    public static string LoginOtherProvider => T("Another provider", "Другой провайдер");

    // How it can end.
    public static string LoginExpired => T("The code is no longer valid. Get a new one and confirm it in the bot.", "Код больше не действует. Получите новый и подтвердите его в боте.");
    public static string LoginNoAnswer => T("{0} is not answering. Check the connection and try again.", "{0} не отвечает. Проверьте интернет и попробуйте ещё раз.");
    public static string LoginMalformed => T("{0} answered something V2pn does not understand. Try again later.", "{0} ответил так, что V2pn не понял ответа. Попробуйте позже.");
    public static string LoginWrongDomain => T("{0} sent a subscription that is not on its own sites, so V2pn did not add it.", "{0} прислал подписку не со своих сайтов, поэтому V2pn её не добавил.");
    public static string LoginAddFailed => T("V2pn could not save the subscription. Try again.", "V2pn не смог сохранить подписку. Попробуйте ещё раз.");
    public static string LoginAdded => T("Subscription {0} added", "Подписка {0} добавлена");
    public static string LoginExists => T("Subscription {0} is already in V2pn; its server is selected.", "Подписка {0} уже есть в V2pn — выбран её сервер.");
}
