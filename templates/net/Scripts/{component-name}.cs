// {name} - V12 component
using V12.Components;
using V12.Basic.Building;

namespace {name-to-lower}.Components
{
    /// <summary>Spins the owning element around its Y axis.</summary>
    public sealed class {component-name} : ScriptableComponent
    {
        /// <summary>Degrees per second applied to the owning element's Y rotation.</summary>
        public float Speed { get; set; } = 45f;

        public override void Update(float deltaTime)
        {
            if (Owner == null) return;
            Owner.GetOrAddTransform().RotationY += Speed * deltaTime;
        }
    }
}
