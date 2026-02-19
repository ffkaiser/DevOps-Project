using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();



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

static string E(string? s) => HtmlEncoder.Default.Encode(s ?? "");
static string Sid(HttpContext ctx) => (string)ctx.Items["sid"]!;

static IResult Html(string html) =>
    Results.Content(html, "text/html; charset=utf-8");


static string Layout(string title, string body, string? username = null)
{
    var userLine = username == null
        ? ""
        : $"<div class='user'>Signed in as <b>{E(username)}</b></div>";

    var html = @"
<!doctype html>
<html>
<head>
<meta charset='utf-8'>
<title>__TITLE__</title>
<style>
body {
    font-family: Arial, sans-serif;
    background-color: #f4f6f8;
    margin: 0;
    padding: 40px;
}
.container {
    background: white;
    padding: 40px;
    border-radius: 10px;
    max-width: 600px;
    margin: auto;
    box-shadow: 0 4px 12px rgba(0,0,0,0.1);
    text-align: center;
}
button {
    padding: 12px 18px;
    margin: 10px;
    font-size: 16px;
    border: none;
    border-radius: 6px;
    cursor: pointer;
}
.primary { background:#007bff; color:white; }
.secondary { background:#6c757d; color:white; }
.user { margin-bottom:20px; color:#555; }
input {
    padding:10px;
    width:70%;
    margin-top:10px;
}
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


app.MapGet("/", (HttpContext ctx) =>
{
    users.TryGetValue(Sid(ctx), out var username);

    var body = @"
<p>Please choose an option:</p>
<button class='primary' onclick=""location.href='/login'"">Login</button>
<button class='secondary' onclick=""location.href='/guest'"">Continue as Guest</button>";

    return Html(Layout("Home", body, username));
});


app.MapGet("/login", () =>
{
    var body = @"
<form method='post'>
<p>Enter your name:</p>
<input name='name' required />
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
        return Results.Redirect("/");
    }

    return Results.Redirect("/login");
});


app.MapGet("/guest", (HttpContext ctx) =>
{
    users[Sid(ctx)] = "Guest-" + Sid(ctx)[..4];
    return Results.Redirect("/");
});

app.Run();
