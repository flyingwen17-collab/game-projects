using UnityEngine;

/// 粒子特效工具：程式生成一次性爆發粒子
public static class ParticleFx
{
    static Material particleMat;

    public static void Burst(Vector3 pos, Color color, int count, float speed, float size, float gravity = 1f, float life = 0.7f)
    {
        var go = new GameObject("fx_burst");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = life;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 8;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = GetMat();

        ps.Emit(count);
        Object.Destroy(go, life + 0.5f);
    }

    static Material GetMat()
    {
        if (particleMat != null) return particleMat;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        particleMat = new Material(shader);
        return particleMat;
    }
}
