namespace Concilium;

// Skill buttons: key = skill name in concilium-markdown.
public record Action4(string skill, string icon, string title, string sub);

public class Strings
{
    // No language switch on screen: the app speaks the Windows language, English if it has no strings for it.
    // CONCILIUM_LANG=<code> overrides it (screenshots).
    public static readonly string Lang = (Environment.GetEnvironmentVariable("CONCILIUM_LANG") is { Length: > 0 } l
        ? l : System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "ru" ? "ru" : "en";
    public static Strings T => Lang == "ru" ? Ru : En;

    public required string Map, Note, NewChat, Chats, Placeholder, AddPerson, AddTitle, Name, Folder, FolderHint, PickFolder,
        Cancel, Add, HelloSub, NoPeople, NoPeopleBtn, Thinking, Stopped, Failed, NeedsLogin, MemBtn, Starting, ByVoice, OpenFile,
        LoginTitle, LoginText, LoginBtn, LoginWaiting, LoginAgain, LoginCode, LoginCodeBtn, LoginFailed, Installing, InstallFailed, NameRequired, FolderMissing;
    public required Func<string, string> Hello;
    public required Func<int, string> Ago;
    public required Action4[] Actions;
    public required Dictionary<string, string> Tool, Mem, Legend;

    // Icon paths: abstract marks, one visual language — circles and curves, no literal pictograms.
    const string INext = "M3.4,18 a1.6,1.6 0 1 0 3.2,0 a1.6,1.6 0 1 0 -3.2,0 M8.4,12.5 a2.6,2.6 0 1 0 5.2,0 a2.6,2.6 0 1 0 -5.2,0 M14.4,6.5 a3.6,3.6 0 1 0 7.2,0 a3.6,3.6 0 1 0 -7.2,0";
    const string IAsk = "M3,12 a6,6 0 1 0 12,0 a6,6 0 1 0 -12,0 M9,12 a6,6 0 1 0 12,0 a6,6 0 1 0 -12,0";
    const string IDeep = "M3,12 a9,9 0 1 0 18,0 a9,9 0 1 0 -18,0 M7,14.5 a5,5 0 1 0 10,0 a5,5 0 1 0 -10,0 M10.4,17 a1.6,1.6 0 1 0 3.2,0 a1.6,1.6 0 1 0 -3.2,0";
    const string IMem = "M13,12 a1,1 0 0 1 2,0 a3,3 0 0 1 -6,0 a5,5 0 0 1 10,0 a7,7 0 0 1 -14,0";

    public static readonly Strings Ru = new()
    {
        Map = "Карта", Note = "Заметка", NewChat = "Новый разговор", Chats = "Разговоры",
        Placeholder = "Расскажите, что беспокоит, или задайте вопрос…", AddPerson = "+ Добавить", AddTitle = "Добавить человека",
        Name = "Имя", Folder = "Папка с заметками о здоровье",
        FolderHint = "Можно не выбирать — тогда заведу новую папку в «Документах».", PickFolder = "Выбрать папку…",
        Cancel = "Отмена", Add = "Добавить", Hello = n => $"Здравствуйте{(n.Length > 0 ? ", " + n : "")}!",
        HelloSub = "Расскажите своими словами, что беспокоит. Или выберите, с чего начать:",
        NoPeople = "Для кого будем вести заметки о здоровье? Добавьте первого человека.", NoPeopleBtn = "Добавить человека",
        Thinking = "Думаю…", Stopped = "Остановлено.", Failed = "Не получилось: ", NeedsLogin = "Нужно войти в Claude.",
        MemBtn = "Обновить память сейчас", Starting = "Готовлюсь…", ByVoice = "  Голосом: Win+H", OpenFile = "Открыть файл",
        LoginTitle = "Подключим помощника",
        LoginText = "Нажмите кнопку — откроется страница Claude. Войдите в свой аккаунт и нажмите «Разрешить» (Authorize). Потом вернитесь в это окно.",
        LoginBtn = "Войти через Claude", LoginWaiting = "Жду подтверждения на странице Claude…", LoginAgain = "Открыть страницу ещё раз",
        LoginCode = "Если страница показала код — вставьте его сюда:", LoginCodeBtn = "Готово", LoginFailed = "Не получилось войти. Попробуйте ещё раз.",
        Installing = "Устанавливаю помощника Claude. Это займёт минуту-две…", InstallFailed = "Не получилось установить помощника. Проверьте интернет и нажмите ещё раз.",
        NameRequired = "Напишите имя.", FolderMissing = "Такой папки нет.",
        Ago = m => m < 1 ? "только что" : m < 60 ? $"{m} мин назад" : m < 1440 ? $"{Math.Round(m / 60.0)} ч назад" : $"{Math.Round(m / 1440.0)} дн назад",
        Actions = new Action4[]
        {
            new("next-step", INext, "Что делать дальше", "Один лучший следующий шаг"),
            new("ask-questions", IAsk, "Задай мне вопросы", "Уточнить то, что важно"),
            new("research", IDeep, "Разберись глубже", "Изучить и обновить картину"),
            new("sleep", IMem, "Запомни разговор", "Сохранить главное"),
        },
        Tool = new() { ["Read"] = "Вспоминаю записи", ["Glob"] = "Вспоминаю записи", ["Grep"] = "Вспоминаю записи", ["WebSearch"] = "Ищу в интернете",
            ["WebFetch"] = "Читаю источник", ["Write"] = "Записываю", ["Edit"] = "Записываю", ["MultiEdit"] = "Записываю", ["Skill"] = "Беру метод",
            ["Task"] = "Поручаю помощнику", ["Agent"] = "Поручаю помощнику", ["TodoWrite"] = "Планирую" },
        Mem = new() { ["running"] = "Обновляю память…", ["done"] = "Память обновлена", ["error"] = "Память не обновилась", ["nothing"] = "Нового для памяти нет" },
        Legend = new() { ["memory"] = "Главное", ["patient facts"] = "Факты", ["hypotheses"] = "Версии", ["questions"] = "Вопросы", ["todo"] = "Планы",
            ["history"] = "История", ["branch"] = "Темы", ["note"] = "Заметки", ["vault"] = "Из хранилища", ["document"] = "Документы", ["missing"] = "Нет заметки" },
    };

    public static readonly Strings En = new()
    {
        Map = "Map", Note = "Note", NewChat = "New conversation", Chats = "Conversations",
        Placeholder = "Tell me what bothers you, or ask a question…", AddPerson = "+ Add", AddTitle = "Add a person",
        Name = "Name", Folder = "Folder with health notes",
        FolderHint = "You can skip this — a new folder will be made in Documents.", PickFolder = "Choose folder…",
        Cancel = "Cancel", Add = "Add", Hello = n => $"Hello{(n.Length > 0 ? ", " + n : "")}!",
        HelloSub = "Tell me in your own words what bothers you. Or pick where to start:",
        NoPeople = "Whose health notes shall we keep? Add the first person.", NoPeopleBtn = "Add a person",
        Thinking = "Thinking…", Stopped = "Stopped.", Failed = "Failed: ", NeedsLogin = "Claude sign-in is needed.",
        MemBtn = "Update memory now", Starting = "Getting ready…", ByVoice = "  By voice: Win+H", OpenFile = "Open file",
        LoginTitle = "Let's connect the assistant",
        LoginText = "Press the button — a Claude page will open. Sign in to your account and press \"Authorize\". Then come back to this window.",
        LoginBtn = "Sign in with Claude", LoginWaiting = "Waiting for confirmation on the Claude page…", LoginAgain = "Open the page again",
        LoginCode = "If the page shows a code, paste it here:", LoginCodeBtn = "Done", LoginFailed = "Sign-in failed. Please try again.",
        Installing = "Installing the Claude assistant. This takes a minute or two…", InstallFailed = "Could not install the assistant. Check the internet connection and press again.",
        NameRequired = "Please enter a name.", FolderMissing = "That folder does not exist.",
        Ago = m => m < 1 ? "just now" : m < 60 ? $"{m} min ago" : m < 1440 ? $"{Math.Round(m / 60.0)} h ago" : $"{Math.Round(m / 1440.0)} d ago",
        Actions = new Action4[]
        {
            new("next-step", INext, "What should I do next", "One best next step"),
            new("ask-questions", IAsk, "Ask me questions", "Clarify what matters"),
            new("research", IDeep, "Dig deeper", "Research and update the picture"),
            new("sleep", IMem, "Remember this talk", "Save what matters"),
        },
        Tool = new() { ["Read"] = "Recalling notes", ["Glob"] = "Recalling notes", ["Grep"] = "Recalling notes", ["WebSearch"] = "Searching the web",
            ["WebFetch"] = "Reading a source", ["Write"] = "Writing down", ["Edit"] = "Writing down", ["MultiEdit"] = "Writing down", ["Skill"] = "Using method",
            ["Task"] = "Asking a helper", ["Agent"] = "Asking a helper", ["TodoWrite"] = "Planning" },
        Mem = new() { ["running"] = "Updating memory…", ["done"] = "Memory updated", ["error"] = "Memory update failed", ["nothing"] = "Nothing new to remember" },
        Legend = new() { ["memory"] = "Overview", ["patient facts"] = "Facts", ["hypotheses"] = "Hypotheses", ["questions"] = "Questions", ["todo"] = "Plans",
            ["history"] = "History", ["branch"] = "Topics", ["note"] = "Notes", ["vault"] = "From the vault", ["document"] = "Documents", ["missing"] = "No note yet" },
    };
}
