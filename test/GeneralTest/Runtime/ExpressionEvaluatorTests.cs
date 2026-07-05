using GalNet.Runtime.Variables;

namespace GeneralTest.Runtime.Variables;

public class ExpressionEvaluatorTests
{
    [Test]
    public void Empty_Condition_Should_Be_True()
    {
        var store = new VariableStore();
        var eval = new ExpressionEvaluator(store);
        Assert.That(eval.EvaluateCondition(""), Is.True);
        Assert.That(eval.EvaluateCondition(null), Is.True);
    }

    [Test]
    public void Literal_Comparison_Should_Work()
    {
        var store = new VariableStore();
        var eval = new ExpressionEvaluator(store);
        Assert.That(eval.EvaluateCondition("1 == 1"), Is.True);
        Assert.That(eval.EvaluateCondition("1 == 2"), Is.False);
    }

    [Test]
    public void Variable_Comparison_Should_Work()
    {
        var store = new VariableStore();
        store.Set("player.score", 100);
        store.Set("player.target", 50);

        var eval = new ExpressionEvaluator(store);
        Assert.That(eval.EvaluateCondition("[player.score] >= [player.target]"), Is.True);
        Assert.That(eval.EvaluateCondition("[player.score] < [player.target]"), Is.False);
    }

    [Test]
    public void Bool_Variable_Should_Work()
    {
        var store = new VariableStore();
        store.Set("player.flag", true);

        var eval = new ExpressionEvaluator(store);
        Assert.That(eval.EvaluateCondition("[player.flag] == true"), Is.True);
        Assert.That(eval.EvaluateCondition("[player.flag] == false"), Is.False);
    }

    [Test]
    public void String_Comparison_Should_Work()
    {
        var store = new VariableStore();
        store.Set("player.route", "alice");

        var eval = new ExpressionEvaluator(store);
        Assert.That(eval.EvaluateCondition("[player.route] == \"alice\""), Is.True);
        Assert.That(eval.EvaluateCondition("[player.route] == \"bob\""), Is.False);
    }

    [Test]
    public void Arithmetic_Should_Work()
    {
        var store = new VariableStore();
        var eval = new ExpressionEvaluator(store);

        // 10 * 3 = 30
        var result = eval.Evaluate("10 * 3");
        Assert.That(result, Is.EqualTo(30));

        // [score] + 10 * [mult]  with score=10, mult=3 → 10 + 10 * 3 = 40
        store.Set("score", 10);
        store.Set("mult", 3);
        var result2 = eval.Evaluate("[score] + 10 * [mult]");
        Assert.That(result2, Is.EqualTo(40));
    }

    [Test]
    public void Unknown_Variable_Should_Be_Null()
    {
        var store = new VariableStore();
        var eval = new ExpressionEvaluator(store);
        Assert.That(eval.EvaluateCondition("[unknown] == \"test\""), Is.False);
    }

    [Test]
    public void Logical_And_Or_Should_Work()
    {
        var store = new VariableStore();
        store.Set("a", true);
        store.Set("b", false);

        var eval = new ExpressionEvaluator(store);
        Assert.That(eval.EvaluateCondition("[a] && [b]"), Is.False);
        Assert.That(eval.EvaluateCondition("[a] || [b]"), Is.True);
    }
}
