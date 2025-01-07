using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public class Player : MonoBehaviour {

	[SerializeField]
	Transform playerInputSpace = default, ball = default;

	#region Serialized Player Movement Stats

	[SerializeField]
	float
		baseTopSpeed = 7.75f,
		dashTopSpeed = 13.5f,
		maxClimbSpeed = 4.0f,
		maxSwimSpeed = 5.0f;

	[SerializeField]
	float 
		// Player movement acceleration.
		groundAcceleration = 67.5f,
		groundDeceleration = 50.0f,
		aerialAcceleration = 20.0f,
		aerialDeceleration = 20.0f,

		// Climbing acceleration. c:
		maxClimbAcceleration = 20f,  
		// Swimming acceleration. c:
		maxSwimAcceleration = 5f;

	[SerializeField, Range(0f, 20f)]
	float jumpHeight = 2.35f;

	[SerializeField, Range(0, 2)]
	int maxAirJumps = 1;

	#endregion

	[SerializeField, Range(0, 90)]
	float 
		maxGroundAngle = 15.0f, 
		maxStairsAngle = 50.0f;

	[SerializeField, Range(90, 170)]
	float maxClimbAngle = 140f;

	[SerializeField, Range(0f, 100f)]
	float maxSnapSpeed = 100f;

	[SerializeField, Min(0f)]
	float probeDistance = 1f;

	#region Serialized Player Swim Stats

	[SerializeField]
	float submergenceOffset = 0.5f;

	[SerializeField, Min(0.1f)]
	float submergenceRange = 1f;

	[SerializeField, Min(0f)]
	float buoyancy = 1f;

	[SerializeField, Range(0f, 10f)]
	float waterDrag = 1f;

	[SerializeField, Range(0.01f, 1f)]
	float swimThreshold = 0.5f;

	#endregion

	[SerializeField]
	LayerMask 
		probeMask = -1, 
		stairsMask = -1, 
		climbMask = -1,
		waterMask = 0;

	[SerializeField]
	Material 
		normalMaterial = default,
		climbingMaterial = default,
		swimmingMaterial = default;

	[SerializeField, Min(0.1f)]
	float ballRadius = 1f;

	[SerializeField, Min(0f)]
	float ballAlignSpeed = 180f;

	[SerializeField, Min(0f)]
	float 
		ballAirRotation = 0.5f, 
		ballSwimRotation = 2f;

	[SerializeField, Min(0f)] 
	float coyoteTimer = 0.0f;

	Rigidbody body, connectedBody, previousConnectedBody;

	#region Vector3 Variables

	Vector3 playerInput;

	Vector3 velocity, connectionVelocity;

	Vector3 connectionWorldPosition, connectionLocalPosition;
	
	Vector3 upAxis, rightAxis, forwardAxis;

	Vector3 contactNormal, steepNormal, climbNormal, lastClimbNormal;

	Vector3 lastContactNormal, lastSteepNormal, lastConnectionVelocity;

	#endregion

	bool desiredJump, desiresClimbing;

	int groundContactCount, steepContactCount, climbContactCount;

	#region CatlikeCoding Tutorial States!

	bool OnGround => groundContactCount > 0;
	bool OnSteep => steepContactCount > 0;
	bool Climbing => climbContactCount > 0 && stepsSinceLastJump > 2;
	bool InWater => submergence > 0f;
	bool Swimming => submergence >= swimThreshold;

	#endregion

	// Bools to check for default and dash state (likely will be replaced)!
	bool DefaultState, DashState;

	float submergence;

	int jumpPhase;

	float minGroundDotProduct, minStairsDotProduct, minClimbDotProduct;

	int stepsSinceLastGrounded, stepsSinceLastJump;

	MeshRenderer meshRenderer;


	// This prevents the ground snapping from affecting your jump height.
	public void PreventSnapToGround () {
		stepsSinceLastJump = -1;
	}

    #region Start-up Functions

    // This loosely verifies angles for various collision types.
    void OnValidate () {
		minGroundDotProduct = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
		minStairsDotProduct = Mathf.Cos(maxStairsAngle * Mathf.Deg2Rad);
		minClimbDotProduct = Mathf.Cos(maxClimbAngle * Mathf.Deg2Rad);
	}

	void Awake () {
		body = GetComponent<Rigidbody>();
		body.useGravity = false;
		meshRenderer = ball.GetComponent<MeshRenderer>();
		OnValidate();
		DefaultState = true;
		DashState = false;
	}

    #endregion

    void Update () {
		// This is for player controls and accounts for being in water or alterations to gravity as well!
		playerInput.x = Input.GetAxis("Horizontal");
		playerInput.z = Input.GetAxis("Vertical");
		playerInput.y = Swimming ? Input.GetAxis("UpDown") : 0f;
		playerInput = Vector3.ClampMagnitude(playerInput, 1f);

		if (playerInputSpace) {
			rightAxis = ProjectDirectionOnPlane(playerInputSpace.right, upAxis);
			forwardAxis =
				ProjectDirectionOnPlane(playerInputSpace.forward, upAxis);
		}

		else {
			rightAxis = ProjectDirectionOnPlane(Vector3.right, upAxis);
			forwardAxis = ProjectDirectionOnPlane(Vector3.forward, upAxis);
		}
		
		if (Swimming) {
			desiresClimbing = false;
		}
		else {
			desiredJump |= Input.GetButtonDown("Jump");
			desiresClimbing = Input.GetButton("Climb");
		}

		#region Default/Run State Bools

		// Default state check.
		if (DefaultState) {
			Debug.Log("Base speed is being used.");
			if (Input.GetButtonDown("Dash")) {
				DefaultState = false;
				DashState = true;
			}
		}

		else if (DashState) {
			Debug.Log("You are in dash state!");
			if (Input.GetButtonDown("Brake")) {
				DashState = false;
				DefaultState = true;
			}
		}

		#endregion
		
	UpdateBall();
	}

	#region CatlikeCoding Ball Rotation Tutorial

	void UpdateBall () {
		// Handles rotation and materials of child "ball" object that rotates with character movement.
		Material ballMaterial = normalMaterial;
		Vector3 rotationPlaneNormal = lastContactNormal;
		float rotationFactor = 1f;
		if (Climbing) {
			ballMaterial = climbingMaterial;
		}
		else if (Swimming) {
			ballMaterial = swimmingMaterial;
			rotationFactor = ballSwimRotation;
		}
		else if (!OnGround) {
			if (OnSteep) {
				rotationPlaneNormal = lastSteepNormal;
			}
			else {
				rotationFactor = ballAirRotation;
			}
		}
		meshRenderer.material = ballMaterial;

		Vector3 movement =
			(body.velocity - lastConnectionVelocity) * Time.deltaTime;
		movement -=
			rotationPlaneNormal * Vector3.Dot(movement, rotationPlaneNormal);

		float distance = movement.magnitude;

		Quaternion rotation = ball.localRotation;
		if (connectedBody && connectedBody == previousConnectedBody) {
			rotation = Quaternion.Euler(
				connectedBody.angularVelocity * (Mathf.Rad2Deg * Time.deltaTime)
			) * rotation;
			if (distance < 0.001f) {
				ball.localRotation = rotation;
				return;
			}
		}
		else if (distance < 0.001f) {
			return;
		}

		float angle = distance * rotationFactor * (180f / Mathf.PI) / ballRadius;
		Vector3 rotationAxis =
			Vector3.Cross(rotationPlaneNormal, movement).normalized;
		rotation = Quaternion.Euler(rotationAxis * angle) * rotation;
		if (ballAlignSpeed > 0f) {
			rotation = AlignBallRotation(rotationAxis, rotation, distance);
		}
		ball.localRotation = rotation;
	}

	Quaternion AlignBallRotation (
		Vector3 rotationAxis, Quaternion rotation, float traveledDistance
	) {
		Vector3 ballAxis = ball.up;
		float dot = Mathf.Clamp(Vector3.Dot(ballAxis, rotationAxis), -1f, 1f);
		float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
		float maxAngle = ballAlignSpeed * traveledDistance;

		Quaternion newAlignment =
			Quaternion.FromToRotation(ballAxis, rotationAxis) * rotation;
		if (angle <= maxAngle) {
			return newAlignment;
		}
		else {
			return Quaternion.SlerpUnclamped(
				rotation, newAlignment, maxAngle / angle
			);
		}
	}

	#endregion

	void FixedUpdate () {
		Vector3 gravity = CustomGravity.GetGravity(body.position, out upAxis);
		UpdateState();

		if (InWater) {
			velocity *= 1f - waterDrag * submergence * Time.deltaTime;
		}

		// Load-bearing velocity function call. 
		AdjustVelocity();

		if (desiredJump) {
			desiredJump = false;
			Jump(gravity);
		}

		if (Climbing) {
			velocity -=
				contactNormal * (maxClimbAcceleration * 0.9f * Time.deltaTime);
		}
		else if (InWater) {
			velocity +=
				gravity * ((1f - buoyancy * submergence) * Time.deltaTime);
		}
		// This helps prevent sliding down slopes without player input.
		else if (OnGround && velocity.sqrMagnitude < 0.01f) {
			velocity +=
				contactNormal *
				(Vector3.Dot(gravity, contactNormal) * Time.deltaTime);
		}
		else if (desiresClimbing && OnGround) {
			velocity +=
				(gravity - contactNormal * (maxClimbAcceleration * 0.9f)) *
				Time.deltaTime;
		}
		else {
			velocity += gravity * Time.deltaTime;
		}

		CoyoteManager();

		// This will update the Rigidbody with this new velocity!
		body.velocity = velocity;
		ClearState();
	}

	#region CatlikeCoding Tutorial State Handlers

	void ClearState () {
		lastContactNormal = contactNormal;
		lastSteepNormal = steepNormal;
		lastConnectionVelocity = connectionVelocity;
		groundContactCount = steepContactCount = climbContactCount = 0;
		contactNormal = steepNormal = climbNormal = Vector3.zero;
		connectionVelocity = Vector3.zero;
		previousConnectedBody = connectedBody;
		connectedBody = null;
		submergence = 0f;
	}

	void UpdateState () {
		stepsSinceLastGrounded += 1;
		stepsSinceLastJump += 1;
		velocity = body.velocity;
		if (
			CheckClimbing() || CheckSwimming() || 
			OnGround || SnapToGround() || CheckSteepContacts()
		) {
			stepsSinceLastGrounded = 0;
			if (stepsSinceLastJump > 1) {
				jumpPhase = 0;
			}
			if (groundContactCount > 1) {
				contactNormal.Normalize();
			}
		}
		else {
			contactNormal = upAxis;
		}
		
		if (connectedBody) {
			if (connectedBody.isKinematic || connectedBody.mass >= body.mass) {
				UpdateConnectionState();
			}
		}
	}

	void UpdateConnectionState () {
		if (connectedBody == previousConnectedBody) {
			Vector3 connectionMovement =
				connectedBody.transform.TransformPoint(connectionLocalPosition) - 
				connectionWorldPosition;
			connectionVelocity = connectionMovement / Time.deltaTime;
		}
		connectionWorldPosition = body.position;
		connectionLocalPosition = connectedBody.transform.InverseTransformPoint(
			connectionWorldPosition
		);
	}

	bool CheckClimbing () {
		if (Climbing) {
			if (climbContactCount > 1) {
				climbNormal.Normalize();
				float upDot = Vector3.Dot(upAxis, climbNormal);
				if (upDot >= minGroundDotProduct) {
					climbNormal = lastClimbNormal;
				}
			}
			groundContactCount = 1;
			contactNormal = climbNormal;
			return true;
		}
		return false;
	}

	bool CheckSwimming () {
		if (Swimming) {
			groundContactCount = 0;
			contactNormal = upAxis;
			return true;}
		return false;
	}

    #endregion

    #region Ground Contact
    bool SnapToGround () {
		if (stepsSinceLastGrounded > 1 || stepsSinceLastJump <= 2 || InWater) {
			return false;
		}
		float speed = velocity.magnitude;
		if (speed > maxSnapSpeed) {
			return false;
		}
		if (!Physics.Raycast(
			body.position, -upAxis, out RaycastHit hit,
			probeDistance, probeMask, QueryTriggerInteraction.Ignore
		)) {
			return false;
		}

		float upDot = Vector3.Dot(upAxis, hit.normal);
		if (upDot < GetMinDot(hit.collider.gameObject.layer)) {
			return false;
		}

		groundContactCount = 1;
		contactNormal = hit.normal;
		// This aligns our speed with the ground when we make contact(?)
		float dot = Vector3.Dot(velocity, hit.normal);
		if (dot > 0f) {
			velocity = (velocity - hit.normal * dot).normalized * speed;
		}
		connectedBody = hit.rigidbody;
		return true;
	}

	bool CheckSteepContacts () {
		if (steepContactCount > 1) {
			steepNormal.Normalize();
			float upDot = Vector3.Dot(upAxis, steepNormal);
			if (upDot >= minGroundDotProduct) {
				steepContactCount = 0;
				groundContactCount = 1;
				contactNormal = steepNormal;
				return true;
			}
		}
		return false;
	}

    #endregion

    #region Adjust Velocity

    // This controls acceleration in various player states.
    void AdjustVelocity () {
		float acceleration, deceleration, speed; // < add "deceleration" here to define context.
		Vector3 xAxis, zAxis;

        if (Climbing) {
			acceleration = maxClimbAcceleration;
			deceleration = maxClimbAcceleration; // <
			speed = maxClimbSpeed;
			xAxis = Vector3.Cross(contactNormal, upAxis);
			zAxis = upAxis;
		}
		// Movement values to pull from while swimming.
		else if (InWater) {
			float swimFactor = Mathf.Min(1f, submergence / swimThreshold);
			acceleration = Mathf.LerpUnclamped(
				OnGround ? groundAcceleration : aerialAcceleration,
				maxSwimAcceleration, swimFactor
			);
			deceleration = Mathf.LerpUnclamped(
				OnGround ? groundDeceleration : aerialDeceleration, // <
				maxSwimAcceleration, swimFactor
			);
			speed = Mathf.LerpUnclamped(baseTopSpeed, maxSwimSpeed, swimFactor);
			xAxis = rightAxis;
			zAxis = forwardAxis;
		}
		// This controls the stats for my movement in DashState.
		else if (DashState) {
			acceleration = OnGround ? groundAcceleration : aerialAcceleration;
			deceleration = OnGround ? groundAcceleration : aerialDeceleration; // <
			speed = dashTopSpeed;
			xAxis = rightAxis;
			zAxis = forwardAxis;
        }
        // This controls the stats for my movement in my BaseState.
        else {
			acceleration = OnGround ? groundAcceleration : aerialAcceleration;
			deceleration = OnGround ? groundDeceleration : aerialDeceleration; // <
			speed = OnGround && desiresClimbing ? maxClimbSpeed : baseTopSpeed;
			xAxis = rightAxis;
			zAxis = forwardAxis;
		}
		xAxis = ProjectDirectionOnPlane(xAxis, contactNormal);
		zAxis = ProjectDirectionOnPlane(zAxis, contactNormal);

		Vector3 relativeVelocity = velocity - connectionVelocity;

		// This handles axial bias on player input.
		Vector3 adjustment;
		adjustment.x =
			playerInput.x * speed - Vector3.Dot(relativeVelocity, xAxis);
		adjustment.z =
			playerInput.z * speed - Vector3.Dot(relativeVelocity, zAxis);
		adjustment.y = Swimming ?
			playerInput.y * speed - Vector3.Dot(relativeVelocity, upAxis) : 0f;
		
		// This checks for player directional input.
		bool horizontal = !Mathf.Approximately(Input.GetAxis("Horizontal"), 0.0f);
        bool vertical = !Mathf.Approximately(Input.GetAxis("Vertical"), 0.0f);
		// This applies acceleration to our character to move them towards our input direction.
        if (horizontal || vertical) {
			adjustment =
				Vector3.ClampMagnitude(adjustment, acceleration * Time.deltaTime);
		}
        else {
            adjustment =
                Vector3.ClampMagnitude(adjustment, deceleration * Time.deltaTime);
		}

		// This moves our player taking into account the removal for axial bias.
		velocity += xAxis * adjustment.x + zAxis * adjustment.z;
		// This moves our player with reference to swimming modifiers.
		if (Swimming) {
			velocity += upAxis * adjustment.y;
		}
	}

    #endregion

	// Manages coyote timer.
	void CoyoteManager()
	{
		if (OnGround) {
			coyoteTimer = 0.0f;
		}
		if (!OnGround) {
			coyoteTimer += Time.deltaTime;
		}
	}

    #region Jump Control
    void Jump (Vector3 gravity) {
		// This controls the direction your jump sends you.
		Vector3 jumpDirection;

		// This controls jump direction while on the ground.
		if (OnGround) {
			jumpDirection = contactNormal;
		}
		// This controls the jump direction while on deeper slopes.
		else if (OnSteep) {
			jumpDirection = steepNormal;
			// This controls air jump recovery from wall jumps
			jumpPhase = 0;
		}
		else if (maxAirJumps > 0 && jumpPhase <= maxAirJumps) {
			// This controls how many jumps you get when leaving a surface without jumping. 
			if (jumpPhase == 0 && coyoteTimer >= 0.2f) {
				jumpPhase = 1;
			}
			jumpDirection = contactNormal;
		}
		else {
			return;
		}

		stepsSinceLastJump = 0;
		// This adds to our current total jump count.
		jumpPhase += 1;
		// This determines desired jump height while accounting for force needed to overcome gravity.
		float jumpSpeed = Mathf.Sqrt(2f * gravity.magnitude * jumpHeight);
		if (InWater) {
			jumpSpeed *= Mathf.Max(0f, 1f - submergence / swimThreshold);
		}
		jumpDirection = (jumpDirection + upAxis).normalized;
		float alignedSpeed = Vector3.Dot(velocity, jumpDirection);
		// This prevents your jump from losing momentum if an upward force exceeds the jump force.
		if (alignedSpeed > 0f) {
			jumpSpeed = Mathf.Max(jumpSpeed - alignedSpeed, 0f);
		}
		// This prevents the double jump from not giving much height if you jump late into a fall.
		else if (alignedSpeed < 0f) {
			jumpSpeed -= alignedSpeed;
		}
		
		velocity += jumpDirection * jumpSpeed;

	}
	#endregion

	#region Collision Handlers
	void OnCollisionEnter (Collision collision) {
		EvaluateCollision(collision);
	}

	void OnCollisionStay (Collision collision) {
		EvaluateCollision(collision);
	}

	void EvaluateCollision (Collision collision) {
		if (Swimming) {
			return;
		}
		int layer = collision.gameObject.layer;
		float minDot = GetMinDot(layer);
		for (int i = 0; i < collision.contactCount; i++) {
			Vector3 normal = collision.GetContact(i).normal;
			float upDot = Vector3.Dot(upAxis, normal);
			if (upDot >= minDot) {
				groundContactCount += 1;
				contactNormal += normal;
				connectedBody = collision.rigidbody;
			}
			else {
				if (upDot > -0.01f) {
					steepContactCount += 1;
					steepNormal += normal;
					if (groundContactCount == 0) {
						connectedBody = collision.rigidbody;
					}
				}
				if (
					desiresClimbing && upDot >= minClimbDotProduct &&
					(climbMask & (1 << layer)) != 0
				) {
					climbContactCount += 1;
					climbNormal += normal;
					lastClimbNormal = normal;
					connectedBody = collision.rigidbody;
				}
			}
		}
	}

	void OnTriggerEnter (Collider other) {
		if ((waterMask & (1 << other.gameObject.layer)) != 0) {
			EvaluateSubmergence(other);
		}
	}

	void OnTriggerStay (Collider other) {
		if ((waterMask & (1 << other.gameObject.layer)) != 0) {
			EvaluateSubmergence(other);
		}
	}
	#endregion
	
	#region Misc Functions

	void EvaluateSubmergence (Collider collider) {
		if (Physics.Raycast(
			body.position + upAxis * submergenceOffset,
			-upAxis, out RaycastHit hit, submergenceRange + 1f,
			waterMask, QueryTriggerInteraction.Collide
		)) {
			submergence = 1f - hit.distance / submergenceRange;
		}
		else {
			submergence = 1f;
		}
		if (Swimming) {
			connectedBody = collider.attachedRigidbody;
		}
	}

	// Basically ProjectOnContactPlane() but it's now compatible with different gravity orientations and appropriate relative movement.
	Vector3 ProjectDirectionOnPlane (Vector3 direction, Vector3 normal) {
		return (direction - normal * Vector3.Dot(direction, normal)).normalized;
	}

	float GetMinDot (int layer) {
		return (stairsMask & (1 << layer)) == 0 ?
			minGroundDotProduct : minStairsDotProduct;
	}

	#endregion

}
