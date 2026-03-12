using System.Text.Encodings.Web;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

//////////////////////////////////////////////////
// SESSION
//////////////////////////////////////////////////

app.Use(async (ctx, next) =>
{
    const string CookieName = "qid";

    if (!ctx.Request.Cookies.TryGetValue(CookieName, out var sid))
    {
        sid = Guid.NewGuid().ToString("N");
        ctx.Response.Cookies.Append(CookieName, sid);
        ctx.Items["sid"] = sid;
    }
    else
    {
        ctx.Items["sid"] = sid;
    }

    await next();
});

var users = new Dictionary<string, string>();
var quizzes = new Dictionary<string, QuizState>();
var scores = new List<Score>();

static string Sid(HttpContext ctx) => (string)ctx.Items["sid"]!;
static string E(string? s) => HtmlEncoder.Default.Encode(s ?? "");

static IResult Html(string html) =>
    Results.Content(html, "text/html; charset=utf-8");

//////////////////////////////////////////////////
// LAYOUT
//////////////////////////////////////////////////

static string Layout(string title, string body, string? username = null)
{
    var userLine = username == null ? "" : $"<div class='user'>Signed in as <b>{E(username)}</b></div>";

    var html = @"
<!doctype html>
<html>
<head>
<meta charset='utf-8'>
<title>__TITLE__</title>
<style>
body{font-family:Arial;background:#f4f6f8;padding:40px}
.container{background:white;padding:40px;border-radius:10px;max-width:700px;margin:auto;box-shadow:0 4px 12px rgba(0,0,0,.1);text-align:center}
button{padding:12px 18px;margin:10px;font-size:16px;border:none;border-radius:6px;cursor:pointer}
.primary{background:#007bff;color:white}
.secondary{background:#6c757d;color:white}
.correct{color:green;font-weight:bold}
.wrong{color:red;font-weight:bold}
.user{margin-bottom:20px}
</style>
</head>
<body>
<div class='container'>
<h1>Quiz Application</h1>
__USER__
__BODY__
</div>
</body>
</html>";

    return html.Replace("__TITLE__", title)
               .Replace("__BODY__", body)
               .Replace("__USER__", userLine);
}

//////////////////////////////////////////////////
// HOME
//////////////////////////////////////////////////

app.MapGet("/", (HttpContext ctx) =>
{
    users.TryGetValue(Sid(ctx), out var username);

    var body = @"
<p>Please choose an option:</p>
<button class='primary' onclick=""location.href='/login'"">Login</button>
<button class='secondary' onclick=""location.href='/guest'"">Continue as Guest</button>";

    return Html(Layout("Home", body, username));
});

//////////////////////////////////////////////////
// LOGIN
//////////////////////////////////////////////////

app.MapGet("/login", () =>
{
    var body = @"
<form method='post'>
<p>Enter your name:</p>
<input name='name' required/>
<br><br>
<button class='primary'>Continue</button>
<button type='button' class='secondary' onclick=""location.href='/'"">Return</button>
</form>";

    return Html(Layout("Login", body));
});

app.MapPost("/login", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var name = form["name"].ToString().Trim();

    if (!string.IsNullOrWhiteSpace(name))
    {
        users[Sid(ctx)] = name;
        return Results.Redirect("/menu");
    }

    return Results.Redirect("/login");
});

app.MapGet("/guest", (HttpContext ctx) =>
{
    users[Sid(ctx)] = "Guest-" + Sid(ctx)[..4];
    return Results.Redirect("/menu");
});

//////////////////////////////////////////////////
// MENU
//////////////////////////////////////////////////

app.MapGet("/menu", (HttpContext ctx) =>
{
    users.TryGetValue(Sid(ctx), out var username);

    var body = @"
<p>Main Menu</p>
<button class='primary' onclick=""location.href='/categories'"">Start Quiz</button>
<button class='secondary' onclick=""location.href='/scores'"">Top Scores</button>
<button class='secondary' onclick=""location.href='/logout'"">Logout</button>";

    return Html(Layout("Menu", body, username));
});

//////////////////////////////////////////////////
// CATEGORIES
//////////////////////////////////////////////////

string[] categories = { "History", "Geography", "Mathematics", "General Knowledge" };

app.MapGet("/categories", (HttpContext ctx) =>
{
    users.TryGetValue(Sid(ctx), out var username);

    var body = "<p>Select Category</p>";

    foreach (var c in categories)
        body += $"<button class='primary' onclick=\"location.href='/difficulty/{Uri.EscapeDataString(c)}'\">{c}</button>";

    body += "<br><button class='secondary' onclick=\"location.href='/menu'\">Return</button>";

    return Html(Layout("Categories", body, username));
});

//////////////////////////////////////////////////
// DIFFICULTY
//////////////////////////////////////////////////

app.MapGet("/difficulty/{category}", (HttpContext ctx, string category) =>
{
    users.TryGetValue(Sid(ctx), out var username);

    var body = $@"
<p>{category}</p>
<button class='primary' onclick=""location.href='/start/{category}/easy'"">Easy</button>
<button class='primary' onclick=""location.href='/start/{category}/hard'"">Hard</button>
<br><button class='secondary' onclick=""location.href='/categories'"">Return</button>";

    return Html(Layout("Difficulty", body, username));
});

//////////////////////////////////////////////////
// START QUIZ
//////////////////////////////////////////////////

app.MapGet("/start/{category}/{difficulty}", (HttpContext ctx, string category, string difficulty) =>
{
    quizzes[Sid(ctx)] = new QuizState(category, difficulty, QuestionBank.Get(category, difficulty));
    return Results.Redirect("/question");
});

//////////////////////////////////////////////////
// SHOW QUESTION
//////////////////////////////////////////////////

app.MapGet("/question", (HttpContext ctx) =>
{
    var quiz = quizzes[Sid(ctx)];
    users.TryGetValue(Sid(ctx), out var username);

    if (quiz.Index >= quiz.Questions.Count)
        return Results.Redirect("/result");

    var q = quiz.Questions[quiz.Index];

    var body = $"<p>{q.Text}</p><form method='post'>";

    for (int i = 0; i < q.Answers.Count; i++)
        body += $"<input type='radio' name='a' value='{i}' required>{q.Answers[i]}<br>";

    body += "<br><button class='primary'>Submit</button></form>";

    return Html(Layout("Question", body, username));
});

//////////////////////////////////////////////////
// ANSWER
//////////////////////////////////////////////////

app.MapPost("/question", async (HttpContext ctx) =>
{
    var quiz = quizzes[Sid(ctx)];
    var form = await ctx.Request.ReadFormAsync();

    int choice = int.Parse(form["a"]);
    quiz.Answer(choice);

    return Results.Redirect("/question");
});

//////////////////////////////////////////////////
// RESULT + REVIEW
//////////////////////////////////////////////////

app.MapGet("/result", (HttpContext ctx) =>
{
    var quiz = quizzes[Sid(ctx)];
    users.TryGetValue(Sid(ctx), out var username);

    scores.Add(new Score(username!, quiz.Category, quiz.Score, quiz.Questions.Count));

    var body = $"<h2>Score {quiz.Score}/{quiz.Questions.Count}</h2>";

    foreach (var r in quiz.Review)
    {
        body += "<hr>";
        body += r.Correct ? "<div class='correct'>Correct</div>" : "<div class='wrong'>Wrong</div>";
        body += $"Question: {r.Question}<br>";
        body += $"Your answer: {r.UserAnswer}<br>";
        body += $"Correct answer: {r.CorrectAnswer}<br>";
    }

    body += "<br><button class='primary' onclick=\"location.href='/menu'\">Menu</button>";

    return Html(Layout("Result", body, username));
});

//////////////////////////////////////////////////
// SCORES
//////////////////////////////////////////////////

app.MapGet("/scores", (HttpContext ctx) =>
{
    users.TryGetValue(Sid(ctx), out var username);

    var body = "<h2>Top Scores</h2>";

    foreach (var s in scores.OrderByDescending(x => x.Points).Take(10))
        body += $"{s.User} - {s.Category} - {s.Points}/{s.Total}<br>";

    body += "<br><button class='secondary' onclick=\"location.href='/menu'\">Return</button>";

    return Html(Layout("Scores", body, username));
});

//////////////////////////////////////////////////
// LOGOUT
//////////////////////////////////////////////////

app.MapGet("/logout", (HttpContext ctx) =>
{
    users.Remove(Sid(ctx));
    quizzes.Remove(Sid(ctx));
    return Results.Redirect("/");
});

app.Run();

//////////////////////////////////////////////////
// DATA CLASSES
//////////////////////////////////////////////////

class QuizState
{
    public string Category;
    public string Difficulty;
    public List<Question> Questions;
    public int Index;
    public int Score;
    public List<ReviewItem> Review = new();

    public QuizState(string c, string d, List<Question> q)
    { Category = c; Difficulty = d; Questions = q; }

    public void Answer(int choice)
    {
        var q = Questions[Index];
        bool correct = choice == q.Correct;

        if (correct) Score++;

        Review.Add(new ReviewItem(q.Text, q.Answers[choice], q.Answers[q.Correct], correct));

        Index++;
    }
}

class ReviewItem
{
    public string Question;
    public string UserAnswer;
    public string CorrectAnswer;
    public bool Correct;

    public ReviewItem(string q, string u, string c, bool r)
    { Question = q; UserAnswer = u; CorrectAnswer = c; Correct = r; }
}

class Question
{
    public string Text;
    public List<string> Answers;
    public int Correct;

    public Question(string t, string a, string b, string c, string d, int correct)
    {
        Text = t;
        Answers = new() { a, b, c, d };
        Correct = correct;
    }
}

class Score
{
    public string User;
    public string Category;
    public int Points;
    public int Total;

    public Score(string u, string c, int p, int t)
    { User = u; Category = c; Points = p; Total = t; }
}

//////////////////////////////////////////////////
// QUESTION BANK
//////////////////////////////////////////////////

static class QuestionBank
{
    public static List<Question> Get(string category, string difficulty)
    {
        if (category == "Mathematics" && difficulty == "easy")
            return new()
            {
                new("What is 15 + 27?","42","41","43","44",0),
                new("What is 9 × 6?","54","56","52","58",0),
                new("Square root of 64?","8","6","7","9",0),
                new("100 divided by 4?","25","20","30","24",0),
                new("12² equals?","144","124","132","154",0)
            };

        if (category == "Mathematics" && difficulty == "hard")
            return new()
            {
                new("Derivative of x²?","2x","x","x²","2",0),
                new("Integral of 1/x?","ln|x|","1/x²","x","e^x",0),
                new("sin(90°)?","1","0","-1","0.5",0),
                new("Limit of (1+1/n)^n?","e","1","0","Infinity",0),
                new("Determinant [[1,2],[3,4]]?","-2","2","-5","5",0)
            };

        if (category == "History")
            return new()
            {
                new("First US President?","George Washington","Jefferson","Lincoln","Adams",0),
                new("WW2 ended in?","1945","1944","1946","1943",0),
                new("Roman Colosseum location?","Rome","Athens","Paris","Berlin",0),
                new("Battle of Hastings year?","1066","1054","1072","1088",0),
                new("Meiji Restoration year?","1868","1853","1871","1889",0)
            };

        if (category == "Geography")
            return new()
            {
                new("Capital of Canada?","Ottawa","Toronto","Vancouver","Montreal",0),
                new("Largest ocean?","Pacific","Atlantic","Indian","Arctic",0),
                new("Everest range?","Himalayas","Alps","Andes","Rockies",0),
                new("Atacama desert country?","Chile","Peru","Bolivia","Argentina",0),
                new("Strait between Europe and Africa?","Gibraltar","Bosporus","Bering","Hormuz",0)
            };

        return new()
        {
            new("Chemical symbol for gold?","Au","Ag","Gd","Go",0),
            new("Hexagon sides?","6","5","7","8",0),
            new("Closest planet to Sun?","Mercury","Venus","Earth","Mars",0),
            new("Gas essential for respiration?","Oxygen","Nitrogen","CO2","Hydrogen",0),
            new("SI unit of electric current?","Ampere","Volt","Ohm","Watt",0)
        };
    }
}