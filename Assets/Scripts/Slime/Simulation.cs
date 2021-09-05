using UnityEngine;
using UnityEngine.Experimental.Rendering;
using ComputeShaderUtility;

public class Simulation : MonoBehaviour {
	public struct Agent {
		public Vector2 position;
		public float angle;
		public Vector3Int speciesMask;
		int unusedSpeciesChannel;
		public int speciesIndex;
	}
	public enum SpawnMode { Random, Point, InwardCircle, RandomCircle }

	const int updateKernel = 0;
	const int diffuseMapKernel = 1;
	const int colourKernel = 2;

	public ComputeShader compute;
	public ComputeShader drawAgentsCS;

	public SlimeSettings settings;

	public Texture2D wallMask;

	[Header("Display Settings")]
	public bool showAgentsOnly;
	public FilterMode filterMode = FilterMode.Point;
	public GraphicsFormat format = ComputeHelper.defaultGraphicsFormat;


	[SerializeField, HideInInspector] protected RenderTexture trailMap;
	[SerializeField, HideInInspector] protected RenderTexture diffusedTrailMap;
	[SerializeField, HideInInspector] protected RenderTexture displayTexture;
	[SerializeField, HideInInspector] protected RenderTexture wallMaskTexture;

	ComputeBuffer agentBuffer;
	ComputeBuffer settingsBuffer;
	Texture2D colourMapTexture;


	protected virtual void Start() {
		Init();
		transform.GetComponentInChildren<MeshRenderer>().material.mainTexture = displayTexture;
	}

	void Init() {
		// Create render textures
		ComputeHelper.CreateRenderTexture(ref trailMap, settings.width, settings.height, filterMode, format);
		ComputeHelper.CreateRenderTexture(ref diffusedTrailMap, settings.width, settings.height, filterMode, format);
		ComputeHelper.CreateRenderTexture(ref displayTexture, settings.width, settings.height, filterMode, format);
		ComputeHelper.CreateRenderTexture(ref wallMaskTexture, settings.width, settings.height, filterMode, format);

		// Assign textures
		compute.SetTexture(updateKernel, "TrailMap", trailMap);
		compute.SetTexture(diffuseMapKernel, "TrailMap", trailMap);
		compute.SetTexture(diffuseMapKernel, "DiffusedTrailMap", diffusedTrailMap);
		compute.SetTexture(colourKernel, "ColourMap", displayTexture);
		compute.SetTexture(colourKernel, "TrailMap", trailMap);

		if (wallMask != null) {
			ComputeHelper.CopyRenderTexture(wallMask, wallMaskTexture);
			//Graphics.Blit(wallMask, wallMaskTexture);
			print("textureSet");
			compute.SetTexture(updateKernel, "WallMask", wallMaskTexture);
			compute.SetTexture(diffuseMapKernel, "DiffusedWallMask", wallMaskTexture);
			compute.SetTexture(colourKernel, "ColourWallMask", wallMaskTexture);
		}

		// Generate agents with random pos/angle
		Agent[] agents = new Agent[settings.numAgents];
		for (int i = 0; i < agents.Length; i++) {
			Vector2 centre = new Vector2(settings.width / 2, settings.height / 2);
			Vector2 startPos = Vector2.zero;
			float randomAngle = Random.value * Mathf.PI * 2;
			float angle = 0;

			//if (settings.spawnMode == SpawnMode.Point) {
			startPos = centre;
			angle = randomAngle;
			//} else if (settings.spawnMode == SpawnMode.Random) {
			//	startPos = new Vector2(Random.Range(0, settings.width), Random.Range(0, settings.height));
			//	angle = randomAngle;
			//} else if (settings.spawnMode == SpawnMode.InwardCircle) {
			//	startPos = centre + Random.insideUnitCircle * settings.height * 0.5f;
			//	angle = Mathf.Atan2((centre - startPos).normalized.y, (centre - startPos).normalized.x);
			//} else if (settings.spawnMode == SpawnMode.RandomCircle) {
			//	startPos = centre + Random.insideUnitCircle * settings.height * 0.15f;
			//	angle = randomAngle;
			//}

			Vector3Int speciesMask;
			int speciesIndex = 0;
			int numSpecies = settings.speciesSettings.Length;

			if (numSpecies == 1) {
				speciesMask = Vector3Int.one;
			} else {
				int species = Random.Range(1, numSpecies + 1);
				speciesIndex = species - 1;
				speciesMask = new Vector3Int((species == 1) ? 1 : 0, (species == 2) ? 1 : 0, (species == 3) ? 1 : 0);
			}

			agents[i] = new Agent() { position = startPos, angle = angle, speciesMask = speciesMask, speciesIndex = speciesIndex };
		}

		// Set variables for CS
		ComputeHelper.CreateAndSetBuffer<Agent>(ref agentBuffer, agents, compute, "agents", updateKernel);


		compute.SetInt("numAgents", settings.numAgents);
		drawAgentsCS.SetBuffer(0, "agents", agentBuffer);
		drawAgentsCS.SetInt("numAgents", settings.numAgents);


		compute.SetInt("width", settings.width);
		compute.SetInt("height", settings.height);

	}

	void FixedUpdate() {
		for (int i = 0; i < settings.stepsPerFrame; i++) {
			RunSimulation();
		}
	}

	void RunSimulation() {
		var speciesSettings = settings.speciesSettings;
		ComputeHelper.CreateStructuredBuffer(ref settingsBuffer, speciesSettings);
		compute.SetBuffer(updateKernel, "speciesSettings", settingsBuffer);
		compute.SetBuffer(colourKernel, "speciesSettings", settingsBuffer);

		// Assign settings
		compute.SetFloat("deltaTime", Time.fixedDeltaTime);
		compute.SetFloat("time", Time.fixedTime);

		compute.SetFloat("trailWeight", settings.trailWeight);
		compute.SetFloat("decayRate", settings.decayRate);
		compute.SetFloat("diffuseRate", settings.diffuseRate);
		compute.SetInt("numSpecies", speciesSettings.Length);


		ComputeHelper.Dispatch(compute, settings.numAgents, 1, 1, kernelIndex: updateKernel);
		ComputeHelper.Dispatch(compute, settings.width, settings.height, 1, kernelIndex: diffuseMapKernel);

		ComputeHelper.CopyRenderTexture(diffusedTrailMap, trailMap);
	}

	void LateUpdate() {
		if (showAgentsOnly) {
			ComputeHelper.ClearRenderTexture(displayTexture);

			drawAgentsCS.SetTexture(0, "TargetTexture", displayTexture);
			ComputeHelper.Dispatch(drawAgentsCS, settings.numAgents, 1, 1, 0);

		} else {
			ComputeHelper.Dispatch(compute, settings.width, settings.height, 1, kernelIndex: colourKernel);
			//	ComputeHelper.CopyRenderTexture(trailMap, displayTexture);
		}
	}

	void OnDestroy() {

		ComputeHelper.Release(agentBuffer, settingsBuffer);
	}
}
