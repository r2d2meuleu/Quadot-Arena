using Godot;
using System;
using ExtensionMethods;

public partial class Projectile : InterpolatedNode3D
{
	public Node3D owner;
	[Export]
	public ParticlesController fx;
	[Export]
	public string projectileName;
	[Export]
	public bool destroyAfterUse = true;
	[Export]
	public float _lifeTime = 1;
	[Export]
	public float speed = 4f;
	[Export]
	public int rotateSpeed = 0;
	[Export] 
	public int damageMin = 3;
	[Export]
	public int damageMax = 24;
	[Export]
	public int blastDamage = 0;
	[Export]
	public float projectileRadius = .2f;
	[Export]
	public float explosionRadius = 2f;
	[Export]
	public DamageType damageType = DamageType.Generic;
	[Export] 
	public float pushForce = 0f;
	[Export] 
	public string OnDeathSpawn;
	[Export] 
	public string decalMark;
	[Export]
	public string secondaryMark;
	[Export]
	public string SecondaryOnDeathSpawn;
	[Export]
	public string _onFlySound;
	[Export]
	public string _onDeathSound;
	[Export]
	public MultiAudioStream audioStream;

	public uint ignoreSelfLayer = 0;

	//Needed for homing projectires
	public Node3D target = null;
	const float capAngle = 16.875f;

	public bool goingUp = false;

	float time = 0f;
	private Rid Sphere;
	private PhysicsShapeQueryParameters3D SphereCast;
	private PhysicsPointQueryParameters3D PointIntersect;
	public override void _Ready()
	{
		if (!string.IsNullOrEmpty(_onFlySound))
		{
			audioStream.Stream = SoundManager.LoadSound(_onFlySound, true);
			audioStream.Play();
		}

		Sphere = PhysicsServer3D.SphereShapeCreate();
		SphereCast = new PhysicsShapeQueryParameters3D();
		SphereCast.ShapeRid = Sphere;

		PointIntersect = new PhysicsPointQueryParameters3D();
		PointIntersect.CollideWithAreas = true;
		PointIntersect.CollideWithBodies = false;
		PointIntersect.CollisionMask = (1 << GameManager.FogLayer);
	}
	public void EnableQuad()
	{
		damageMin *= GameManager.Instance.QuadMul;
		damageMax *= GameManager.Instance.QuadMul;
		blastDamage *= GameManager.Instance.QuadMul;
		pushForce *= GameManager.Instance.QuadMul * .5f;
	}
	public override void _PhysicsProcess(double delta)
	{
		if (GameManager.Paused)
			return;

		float deltaTime = (float)delta;

		time += deltaTime;

		if (time >= _lifeTime)
		{
			if (!string.IsNullOrEmpty(_onDeathSound))
				SoundManager.Create3DSound(GlobalPosition, SoundManager.LoadSound(_onDeathSound));
			QueueFree();
			return;
		}

		CollisionObject3D Hit = null;
		Vector3 Collision = Vector3.Zero;
		Vector3 Normal = Vector3.Zero;
		Vector3 d = GlobalTransform.ForwardVector();

		var SpaceState = GetWorld3D().DirectSpaceState;
		//check for collision
		{			
			PhysicsServer3D.ShapeSetData(Sphere, projectileRadius);
			SphereCast.CollisionMask = ~(GameManager.NoHitMask | ignoreSelfLayer);
			SphereCast.Motion = -d * speed * deltaTime;
			SphereCast.Transform = GlobalTransform;
			var result = SpaceState.CastMotion(SphereCast);

			if (result[1] < 1)
			{
				SphereCast.Transform = new Transform3D(GlobalTransform.Basis, GlobalTransform.Origin + (SphereCast.Motion * result[1]));
				var hit = SpaceState.GetRestInfo(SphereCast);
				if (hit.Count > 0)
				{
					CollisionObject3D collider = (CollisionObject3D)InstanceFromId((ulong)hit["collider_id"]);
					if (collider != owner)
					{
						Collision = (Vector3)hit["point"];
						Normal = (Vector3)hit["normal"];
						Hit = collider;

						if ((damageType == DamageType.Rocket) || (damageType == DamageType.Grenade) || (damageType == DamageType.Plasma) || (damageType == DamageType.BFGBall))
						{
							Vector3 impulseDir = d.Normalized();

							if (Hit is Damageable damageable)
							{
								switch (damageType)
								{
									case DamageType.BFGBall:
										damageable.Impulse(impulseDir, pushForce);
										damageable.Damage(GD.RandRange(damageMin, damageMax) * 100, damageType, owner);
										break;
									default:
										damageable.Impulse(impulseDir, pushForce);
										damageable.Damage(GD.RandRange(damageMin, damageMax), damageType, owner);
										break;
								}
							}
						}
					}
				}
			}
		}

		//explosion
		if (Hit != null)
		{
			PhysicsServer3D.ShapeSetData(Sphere, explosionRadius);
			SphereCast.CollisionMask = GameManager.TakeDamageMask | (1 << GameManager.RagdollLayer);
			SphereCast.Motion = Vector3.Zero;
			SphereCast.Transform = new Transform3D(GlobalTransform.Basis, Collision);
			var hits = SpaceState.IntersectShape(SphereCast);
			var max = hits.Count;

			for (int i = 0; i < max; i++)
			{
				var hit = hits[i];
				
				CollisionObject3D collider = (CollisionObject3D)hit["collider"];
				if (collider is Damageable damageable)
				{
					Vector3 collision = collider.GlobalPosition;
					var RayCast = PhysicsRayQueryParameters3D.Create(GlobalPosition, collision, (1 << GameManager.ColliderLayer));
					var check = SpaceState.IntersectRay(RayCast);
					if (check.Count == 0)
					{
						Vector3 hPosition = collider.Position;
						Vector3 Distance = (hPosition - Collision);
						float lenght;
						Vector3 impulseDir = Distance.GetLenghtAndNormalize(out lenght);

						switch (damageType)
						{
							case DamageType.Explosion:
							case DamageType.Rocket:
								int damage = blastDamage;
								//in order to enable rocket jump
								if (collider == owner)
									damage = Mathf.CeilToInt(damage / 3);
								damageable.Impulse(impulseDir, Mathf.Lerp(pushForce, 100, lenght / explosionRadius));
								damageable.Damage(Mathf.CeilToInt(Mathf.Lerp(damage, 1, lenght / explosionRadius)), DamageType.Explosion, owner);
								break;
							case DamageType.Plasma:
								if (collider == owner) //Plasma never does self damage
									continue;
								else
									damageable.Damage(GD.RandRange(damageMin, damageMax), damageType, owner);
								break;
							case DamageType.BFGBall:
								if (collider == owner) //BFG never does self damage
									continue;
								else
									damageable.Damage(GD.RandRange(damageMin, damageMax) * 100, damageType, owner);
								break;
							case DamageType.Telefrag:
								damageable.Impulse(impulseDir, Mathf.Lerp(pushForce, 100, lenght / explosionRadius));
								damageable.Damage(blastDamage, DamageType.Telefrag, owner);
								break;
							default:
								damageable.Damage(GD.RandRange(damageMin, damageMax), damageType, owner);
								break;
						}
					}
				}
			}

			if (!string.IsNullOrEmpty(OnDeathSpawn))
			{
				Node3D DeathSpawn = (Node3D)ThingsManager.thingsPrefabs[OnDeathSpawn].Instantiate();
				GameManager.Instance.TemporaryObjectsHolder.AddChild(DeathSpawn);
				DeathSpawn.Position = Collision + d;
				DeathSpawn.SetForward(-d);
				DeathSpawn.Rotate(d, (float)GD.RandRange(0, Mathf.Pi * 2.0f));
				if (fx != null)
				{
					fx.Reparent(DeathSpawn);
					fx.enableLifeTime = true;
				}
			}

			//Check if collider can be marked
			if (CheckIfCanMark(SpaceState, Hit, Collision))
			{
				SpriteController DecalMark = (SpriteController)ThingsManager.thingsPrefabs[decalMark].Instantiate();
				GameManager.Instance.TemporaryObjectsHolder.AddChild(DecalMark);
				DecalMark.GlobalPosition = Collision + (Normal * .03f);
				DecalMark.SetForward(Normal);
				DecalMark.Rotate(Normal, (float)GD.RandRange(0, Mathf.Pi * 2.0f));
				if (Hit is Crusher)
					DecalMark.referenceNode = Hit;
				if (!string.IsNullOrEmpty(secondaryMark))
				{
					SpriteController SecondMark = (SpriteController)ThingsManager.thingsPrefabs[secondaryMark].Instantiate();
					GameManager.Instance.TemporaryObjectsHolder.AddChild(SecondMark);
					SecondMark.GlobalPosition = Collision + (Normal * .05f);
					SecondMark.SetForward(Normal);
					SecondMark.Rotate(Normal, (float)GD.RandRange(0, Mathf.Pi * 2.0f));
					if (Hit is Crusher)
						SecondMark.referenceNode = Hit;
				}
			}
			if (!string.IsNullOrEmpty(_onDeathSound))
				SoundManager.Create3DSound(Collision, SoundManager.LoadSound(_onDeathSound));
			QueueFree();
			return;
		}
		/*
		if (target != null)
		{
			Vector3 aimAt = (target.transform.position - cTransform.position).normalized;
			float angle = Vector3.SignedAngle(aimAt, cTransform.forward, cTransform.up);
			if (Mathf.Abs(angle) > capAngle)
			{
				Quaternion newRot;
				if (angle > 0)
					newRot = Quaternion.AngleAxis(capAngle, cTransform.up);
				else
					newRot = Quaternion.AngleAxis(-capAngle, cTransform.up);
				aimAt = (newRot * cTransform.forward).normalized;
			}
			cTransform.forward = aimAt;
		}

		if (rotateSpeed != 0)
			cTransform.RotateAround(cTransform.position, cTransform.forward, rotateSpeed * deltaTime);

		if (goingUp)
			cTransform.position = cTransform.position + cTransform.up * speed * deltaTime;
		else
		*/
		Position -= d * speed * deltaTime;

	}
	public bool CheckIfCanMark(PhysicsDirectSpaceState3D SpaceState, CollisionObject3D collider, Vector3 collision)
	{
		if (collider is Damageable)
			return false;

		//Don't mark moving platforms
		if (collider is Crusher)
			return false;

		//Check if mapcollider are noMarks
		if (MapLoader.noMarks.Contains(collider))
			return false;

		//Check if collision in inside a fog Area
		PointIntersect.Position = collision;

		var hits = SpaceState.IntersectPoint(PointIntersect);
		if (hits.Count == 0)
			return true;

		return false;
	}
}
