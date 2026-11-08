// {name} - V12 component script
using V12.Components;
using V12.Basic.Building;

namespace {name-to-lower}.Components
{
    /// <summary>Example component extending V12's ECS system.</summary>
    public sealed class DefaultScript : ScriptableComponent
    {
        /// <summary>Degrees per second applied to the owning element's Y rotation.</summary>
        public float Speed { get; set; } = 45f;

        public override void Update(float deltaTime)
        {
            if (Owner == null) return;
            Owner.GetOrAddTransform().RY += Speed * deltaTime;
        }
    }
}
