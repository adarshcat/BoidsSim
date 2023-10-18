using Godot;
using Godot.Collections;
using System;
using System.Threading;

public partial class Main : Control
{
    private MultiMeshInstance2D multiMesh;

    private const int gridSize = 10;
	private const int cellSize = 20;
	private const uint invocations = 20;
	private const int particleCount = 10000; //3000
	private const int boidDispSize = 4;
	private Vector2 dim;

	private float[] particles;

	private RenderingDevice rd;
	private Rid shaderRid;
	private Rid uniformSetRid;

	private Rid settingsRid;
	private Rid gridDataRid;
	private Rid particlesDataRid;
	private Rid mouseRid;

	public override void _Ready(){
        GD.Randomize();

        multiMesh = GetNode<MultiMeshInstance2D>("multiMesh");
        multiMesh.Multimesh.InstanceCount = particleCount;
		GenerateTriangleMesh(multiMesh);

		CalculateDimensions();
		byte[] settings = GetSettings();

		InitialiseParticles();

		int[] gridData = UpdatedGridData();
		byte[] gridBytes = GridToBytes(gridData);

		byte[] particleBytes = ParticleDataToBytes(particles);

		InitialiseShader();
		PackData(settings, gridBytes, particleBytes);
		DispatchShader();

		RetrieveShaderData();
	}

    public override void _Process(double delta){
		int[] gridData = UpdatedGridData();
		byte[] gridBytes = GridToBytes(gridData);

        UpdateShaderData(gridBytes, GetMouseData());

		DispatchShader();

		RetrieveShaderData();
    }

    private void PackData(byte[] settings, byte[] gridBytes, byte[] particleBytes){
		// preparing to pass in the camera data to the compute shader
		settingsRid = rd.StorageBufferCreate((uint)settings.Length, settings);

		RDUniform settingsUniform = new() {
			UniformType = RenderingDevice.UniformType.StorageBuffer,
			Binding = 0
		};
		settingsUniform.AddId(settingsRid);

		//praparing to pass in the grid data to the compute shader
		gridDataRid = rd.StorageBufferCreate((uint)gridBytes.Length, gridBytes);

		RDUniform gridUniform = new() {
			UniformType = RenderingDevice.UniformType.StorageBuffer,
			Binding = 1
		};
		gridUniform.AddId(gridDataRid);

		//praparing to pass in the particles data to the compute shader
		particlesDataRid = rd.StorageBufferCreate((uint)particleBytes.Length, particleBytes);

		RDUniform particlesUniform = new() {
			UniformType = RenderingDevice.UniformType.StorageBuffer,
			Binding = 2
		};
		particlesUniform.AddId(particlesDataRid);

		byte[] mouseBytes = new byte[8];
		mouseRid = rd.StorageBufferCreate((uint)mouseBytes.Length, mouseBytes);

		RDUniform mouseUniform = new() {
			UniformType = RenderingDevice.UniformType.StorageBuffer,
			Binding = 3
		};
		mouseUniform.AddId(mouseRid);

		uniformSetRid = rd.UniformSetCreate(new Array<RDUniform> { settingsUniform, gridUniform, particlesUniform, mouseUniform }, shaderRid, 0);
	}

	private int[] UpdatedGridData(){
		int dimx = (int) dim.X;
		int dimy = (int) dim.Y;
		int[,] gridFillData = new int[dimx, dimy];
		int[] gridData = new int[dimx * dimy * cellSize];

		for (int i=0; i<particleCount; i++){
			int gridx = (int) (particles[i*4] / gridSize);
			int gridy = (int) (particles[i*4+1] / gridSize);
			int currentCellIndex = gridFillData[gridx, gridy];

			if (currentCellIndex >= cellSize) continue; // FALSE SEGMENT OF CODE, MAY RESULT IN NON REALISTIC RESULTS

			gridData[gridy*dimx*cellSize + gridx*cellSize + currentCellIndex] = i+1;
			gridFillData[gridx, gridy] += 1;
		}

		return gridData;
	}

	private void InitialiseParticles(){
		int viewx = (int) GetViewportRect().Size.X;
		int viewy = (int) GetViewportRect().Size.Y;
		particles = new float[particleCount*4];

		for (int i=0; i<particleCount; i++){
			particles[i*4] = GD.Randi() % viewx;
			particles[i*4+1] = GD.Randi() % viewy;
			particles[i*4+2] = (GD.Randf()-0.5f) * 4.0f;
			particles[i*4+3] = (GD.Randf()-0.5f) * 4.0f;
		}
	}

	private byte[] GridToBytes(int[] gridData){
		byte[] gridBytes = new byte[gridData.Length * sizeof(int)];
		Buffer.BlockCopy(gridData, 0, gridBytes, 0, gridBytes.Length);

		return gridBytes;
	}

	private byte[] ParticleDataToBytes(float[] particles){
		byte[] particleBytes = new byte[particles.Length * sizeof(float)];
		Buffer.BlockCopy(particles, 0, particleBytes, 0, particleBytes.Length);

		return particleBytes;
	}

	private void UpdateShaderData(byte[] gridBytes, byte[] mouseBytes){
		//retrieve updated particle data from the shader
		rd.BufferUpdate(gridDataRid, 0, (uint) gridBytes.Length, gridBytes);
		rd.BufferUpdate(mouseRid, 0, (uint) mouseBytes.Length, mouseBytes);
	}

	private void RetrieveShaderData(){
		//retrieving particle data from the shader
		var particleBytes = rd.BufferGetData(particlesDataRid);
		var output = new float[particles.Length];
		Buffer.BlockCopy(particleBytes, 0, output, 0, particleBytes.Length);

		particles = output;

	    for (int i=0; i<multiMesh.Multimesh.InstanceCount; i++){
			Vector2 vel = new Vector2(particles[i*4 + 2], particles[i*4 + 3]);
            Vector2 pos = new Vector2(particles[i*4 + 0], particles[i*4 + 1]);
			float angle = vel.Angle();

			float dotVal = vel.Normalized().Dot(new Vector2(1,0)) + 1;

			//multiMesh.Multimesh.SetInstanceColor(i, new Color((vel.X/dim.X)*100.0f, (vel.Y/dim.Y)*100.0f, 0.0f));
			multiMesh.Multimesh.SetInstanceColor(i, Color.FromHsv(dotVal/2.0f, 0.8f, 1.0f, 1.0f));
            multiMesh.Multimesh.SetInstanceTransform2D(i, new Transform2D(angle, pos));
        }
	}

	private void InitialiseShader(){
		rd = RenderingServer.CreateLocalRenderingDevice();
		RDShaderFile shaderFile = GD.Load<RDShaderFile>("res://shaders/compute.glsl");
		RDShaderSpirV shaderBytecode = shaderFile.GetSpirV();
		shaderRid = rd.ShaderCreateFromSpirV(shaderBytecode);
	}

	private void DispatchShader(){
		Rid pipelineRid = rd.ComputePipelineCreate(shaderRid);
		long computeList = rd.ComputeListBegin();
		rd.ComputeListBindComputePipeline(computeList, pipelineRid);
		rd.ComputeListBindUniformSet(computeList, uniformSetRid, 0);
		rd.ComputeListDispatch(computeList, xGroups: (uint) (dim.X/invocations), yGroups: (uint) (dim.Y/invocations), zGroups: 1);
		rd.ComputeListEnd();

		rd.Submit();
		rd.Sync();
	}

	private void CalculateDimensions(){
        dim = new Vector2();
		dim.X = Mathf.Ceil(GetViewportRect().Size.X / gridSize) + invocations;
		dim.X -= (int) (dim.X) % invocations;
		dim.Y = Mathf.Ceil(GetViewportRect().Size.Y / gridSize) + invocations;
		dim.Y -= (int) (dim.Y) % invocations;
	}

	private void GenerateTriangleMesh(MultiMeshInstance2D multiMesh){
		var vertices = new Vector3[]
		{
			new Vector3(-0.5f*boidDispSize, -1*boidDispSize, 0),
			new Vector3(-0.5f*boidDispSize, 1*boidDispSize, 0),
			new Vector3(1.75f*boidDispSize, 0, 0),
		};

		var arrMesh = new ArrayMesh();
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;

		arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

		multiMesh.Multimesh.Mesh = arrMesh;
	}

	private byte[] GetSettings(){
		//Vector2 viewportSize =  GetViewportRect().Size;
		
		int[] settings = {gridSize, (int) dim.X, (int) dim.Y, cellSize, (int) (dim.X)*gridSize-1, (int) (dim.Y)*gridSize-1};
		byte[] settingsBytes = new byte[settings.Length * sizeof(int)];
		Buffer.BlockCopy(settings, 0, settingsBytes, 0, settingsBytes.Length);

		return settingsBytes;
	}

	private byte[] GetMouseData(){
		float[] data = {};
		if (Input.IsMouseButtonPressed(MouseButton.Left)){
			Vector2 mousePos = GetViewport().GetMousePosition();
			data = new float[]{mousePos.X, mousePos.Y};
		}
		else{
			data = new float[]{696969.0f, 696969.0f};
		}
		
		byte[] dataBytes = new byte[data.Length * sizeof(float)];
		Buffer.BlockCopy(data, 0, dataBytes, 0, dataBytes.Length);

		return dataBytes;
	}

	public override void _Notification(int what){
		if (what == NotificationPredelete){
			CleanupGPU();
		}
	}

	private void CleanupGPU(){
		if (rd == null) return;

		rd.FreeRid(settingsRid);
		settingsRid = new Rid();

		rd.FreeRid(shaderRid);
		shaderRid = new Rid();

        rd.FreeRid(particlesDataRid);
        particlesDataRid = new Rid();

		rd.FreeRid(mouseRid);
        mouseRid = new Rid();

        rd.FreeRid(gridDataRid);
        gridDataRid = new Rid();

		rd.Free();
		rd = null;
	}
}
