namespace TianMu.Island.Physics;

/// <summary>
/// 阻尼彈簧（Damped Harmonic Oscillator）——天幕「果凍感」的來源。
///
///   F = -k · (x - target) - c · v
///
/// k（Stiffness，勁度）越大 → 回彈越快；c（Damping，阻尼）越小 → 晃動越久。
/// 阻尼比 ζ = c / (2·√(k·m))：ζ &lt; 1 會過衝回彈（果凍），ζ = 1 剛好不晃，ζ &gt; 1 黏稠緩慢。
///
/// 採用「半隱式歐拉 + 固定子步長」積分，不管螢幕是 60Hz 還是 240Hz，手感都一致且數值穩定。
/// </summary>
public sealed class Spring
{
    private const double SubStep = 1.0 / 480.0;
    private const double RestEpsilon = 0.005;

    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }

    public double Stiffness { get; set; }
    public double Damping { get; set; }
    public double Mass { get; set; } = 1.0;

    public Spring(double initial, double stiffness, double damping)
    {
        Value = Target = initial;
        Stiffness = stiffness;
        Damping = damping;
    }

    public bool IsAtRest =>
        Math.Abs(Value - Target) < RestEpsilon && Math.Abs(Velocity) < RestEpsilon;

    /// <summary>瞬間移動到某值（無動畫）。</summary>
    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    /// <summary>給彈簧一個瞬時推力，例如點擊時的「Q 彈」。</summary>
    public void Impulse(double velocity) => Velocity += velocity;

    /// <summary>推進時間 dt 秒。</summary>
    public void Step(double dt)
    {
        while (dt > 0)
        {
            double h = Math.Min(dt, SubStep);
            double force = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += force / Mass * h;   // 先更新速度
            Value += Velocity * h;          // 再用新速度更新位置（半隱式，較穩定）
            dt -= h;
        }

        if (IsAtRest)
        {
            Value = Target;
            Velocity = 0;
        }
    }
}
