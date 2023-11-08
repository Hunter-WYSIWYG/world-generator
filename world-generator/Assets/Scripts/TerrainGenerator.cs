using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class TerrainGenerator : MonoBehaviour
{
    public bool realTimeGeneration;
    public bool updateTerrain;
    [Header("Base Settings")]
    //in mesh-squares
    public int terrainLenght = 100;
    //in mesh-squares
    public int terrainDepth = 100;
    public float terrainScaling = 1f;
    public int vertexDensity = 1;

    [Header("Movement Settings")]
    public float noiseOffsetX = 0.0f;
    public float noiseOffsetZ = 0.0f;
    public bool moveTerrainX = false;
    public bool moveTerrainZ = false;
    public float terrainMoveSpeed = 0.4f;

    [Header("Height Settings")]
    //terrain height differences
    public float heightVariety = 10f;

    //terrain steepness
    public float noisePower = 1f;

    //frequency of mountains and valleys
    [Range(0,0.5f)]
    public float heightDensity = 0.05f;

    //height percentage of the generated terrain in which the water plane is set
    [Range(0,1)]
    public float relativeWaterHeight = 0f;

    //lock to prevent plane height from changing when more, randomly higher or lower terrain is generated (plane height is relative!)
    //click once u have edited the height settings
    public bool lockWaterPlaneOnCurrentHeights = false;

    //lock to prevent gradient from changing when more, randomly higher or lower terrain is generated (gradient heights are relative!)
    //click once u have edited the height settings
    public bool lockGradientOnCurrentHeights = false;


    [Header("Noise Layer Settings")]
    //terrain roughness height
    [Range(0,1)]
    public float lowerNoiseLayerHeightVariety = 0.2f;

    //terrain roughness density
    [Range(0,1)]
    public float lowerNoiseLayerDensity = 0.2f;

    //define border where upper noise layer starts beeing applied; [0,1]
    //set 0 to disable
    [Range(0,1)]
    public float upperHeightPercent = 0f;

    //terrain roughness height for upper height area
    [Range(0,1)]
    public float upperNoiseLayerHeightVariety = 0.2f;

    //terrain roughness density for upper height area
    [Range(0,1)]
    public float upperNoiseLayerDensity = 0.2f;
    [Range(0,1)]
    public float noiseLayerBlendingArea = 0f;

    public enum TerrainMaterial {TextureMaterial, ColorMaterial};
    [Header("Terrain Material")]
    public TerrainMaterial terrainMaterial;

    [Header("Texture Settings")]
    [Range(0,4)]
    public float textureScaling;
    [Range(-1,1)]
    public float steepnessIntensity;
    [Range(0,50)]
    public float steepnessBlending;
    [Range(0,4)]
    public float steepnessTextureScaling;

    [Header("Predefined Parameters")]
    public bool generateMountains = false;
    public bool generateIslands = false;

    public enum ColorView {Gradient, Biome, Temp, Prec};
    [Header("Color Settings")]
    public ColorView colorView;
    public Gradient usedGradient;
    public Gradient mountainGradient;
    public Gradient islandGradient;
    public Color waterPlaneColor;

    [Header("Game Objects")]
    public BiomeGenerator biomeGenerator;
    public Material terrainTextureMat;
    public Material terrainColorMat;
    public GameObject player;
    
    private int currentColorMapID = 0;
    private static int gradientMapID = 0;
    private static int biomeMapID = 1;
    private static int temperatureMapID = 2;
    private static int precipitationMapID = 3;
    private int currentLandscapeID = 0;
    private static int mountainsID = 1;
    private static int islandsID = 2;
    private float minTerrainHeight = 0f;
    private float maxTerrainHeight = 0f;
    private int meshVertexLenght = 0;
    private int meshVertexDepth = 0;
    private float gradientMinValue = 0f;
    private float gradientMaxValue = 0f;
    private Vector3[] uncondensedTerrainVertices;
    private Vector3[] terrainVertices;
    private bool meshGenerated = false;
    private Mesh terrainMesh;
    private TerrainMaterial currentTerrainMaterial;
    private float currentTextureScaling;
    private float currentSteepnessIntensity;
    private float currentSteepnessBlending;
    private float currentSteepnessTextureScaling;
    private ColorView currentColorView;
    private bool start = true;

    void Start() {
        terrainMesh = new Mesh();
        terrainMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        GetComponent<MeshFilter>().mesh = terrainMesh;
        GetComponent<MeshCollider>().sharedMesh = terrainMesh;
        lockGradientOnCurrentHeights = true;
        currentTerrainMaterial = terrainMaterial;
        currentTextureScaling = float.MaxValue;
        currentSteepnessIntensity = float.MaxValue;
        currentSteepnessBlending = float.MaxValue;
        currentSteepnessTextureScaling = float.MaxValue;
        currentColorView = colorView;
        if(!realTimeGeneration) {
            updateTerrain = true;
        }
    }

    private void Update() {
        if(updateTerrain || realTimeGeneration) {
            UpdateColorMap();
            SetPredefinedParameters();
            UpdateMesh();
            meshGenerated = true;
            updateTerrain = false;
        }

        if(start) {
            resetPlayerPosition();
            GetComponent<MeshCollider>().convex = false;
            start = false;
        }
    }

    void UpdateMesh() {
        //add necessary nodes to close terrain edges & to indicate terrainLength in vertices
        meshVertexLenght = terrainLenght*vertexDensity + 2 + 1;
        meshVertexDepth = terrainDepth*vertexDensity + 2 + 1;

        //update terrain mesh
        CreateTerrainMesh();
        switchTerrainMaterial();
        scaleTerrainTexture(meshVertexLenght,meshVertexDepth);
        adjustSteepnessIntensity();
        adjustSteepnessBlending();
        adjustSteepnessTexScaling(meshVertexLenght,meshVertexDepth);

        MoveTerrainMesh();
    }

    void resetPlayerPosition() {
        float posX = terrainScaling + (terrainLenght*terrainScaling)/2;
        float posZ = terrainScaling + (terrainDepth*terrainScaling)/2;
        player.transform.position = new Vector3(posX, maxTerrainHeight + terrainScaling, posZ);
    }

    void switchTerrainMaterial() {
        if(terrainMaterial != currentTerrainMaterial) {
            currentTerrainMaterial = terrainMaterial;
            switch (terrainMaterial) {
                case TerrainMaterial.TextureMaterial:
                    GetComponent<MeshRenderer>().material = terrainTextureMat;
                    break;
                case TerrainMaterial.ColorMaterial:
                    GetComponent<MeshRenderer>().material = terrainColorMat;
                    break;
            }
        }
    }

    void adjustSteepnessTexScaling(int meshVertexLenght,int meshVertexDepth) {
        if(currentSteepnessTextureScaling != steepnessTextureScaling) {
            currentSteepnessTextureScaling = steepnessTextureScaling;

            terrainTextureMat.SetVector("TextureScaling_Steepness", new Vector2(meshVertexLenght*steepnessTextureScaling, meshVertexDepth*steepnessTextureScaling));
            if(terrainMaterial == TerrainMaterial.TextureMaterial) {
                GetComponent<MeshRenderer>().material = terrainTextureMat;
            }
        }
    }

    void adjustSteepnessBlending() {
        if(currentSteepnessBlending != steepnessBlending) {
            currentSteepnessBlending = steepnessBlending;

            terrainTextureMat.SetFloat("SteepnessBlending", currentSteepnessBlending);
            if(terrainMaterial == TerrainMaterial.TextureMaterial) {
                GetComponent<MeshRenderer>().material = terrainTextureMat;
            }
        }
    }

    void adjustSteepnessIntensity() {
        if(currentSteepnessIntensity != steepnessIntensity) {
            currentSteepnessIntensity = steepnessIntensity;

            terrainTextureMat.SetFloat("SteepnessIntensity", currentSteepnessIntensity);
            if(terrainMaterial == TerrainMaterial.TextureMaterial) {
                GetComponent<MeshRenderer>().material = terrainTextureMat;
            }
        }
    }

    void scaleTerrainTexture(int meshVertexLenght,int meshVertexDepth) {
        if(currentTextureScaling != textureScaling) {
            currentTextureScaling = textureScaling;

            terrainTextureMat.SetVector("TextureScaling_Terrain", new Vector2(meshVertexLenght*textureScaling, meshVertexDepth*textureScaling));
            if(terrainMaterial == TerrainMaterial.TextureMaterial) {
                GetComponent<MeshRenderer>().material = terrainTextureMat;
            }
        }
    }

    void MoveTerrainMesh() {
        if (moveTerrainX) {
            noiseOffsetX += Time.deltaTime * terrainMoveSpeed;
        }
        if (moveTerrainZ) {
            noiseOffsetZ += Time.deltaTime * terrainMoveSpeed;
        }
    }
    
    void CreateTerrainMesh()
    {
        //every node in the mesh as an 2d array
        uncondensedTerrainVertices = new Vector3[meshVertexLenght*meshVertexDepth];

        //every square in the mesh as 4 nodes (overlapping)
        terrainVertices = new Vector3[4*meshVertexLenght*meshVertexDepth];
        Vector2[] terrainUVs = new Vector2[4*meshVertexLenght*meshVertexDepth];
        int[] terrainTriangles = new int [(meshVertexLenght-1) * (meshVertexDepth-1) * 6];
        Color[] terrainColors = new Color[terrainVertices.Length];

        terrainVertices = CalcTerrainVertices(terrainVertices);
        terrainUVs = CalcTerrainUVs(terrainUVs);
        terrainTriangles = CalcTerrainTriangles(terrainTriangles);
        terrainColors = CalcTerrainColors(terrainColors, terrainVertices);

        terrainMesh.Clear();
        terrainMesh.vertices = terrainVertices;
        terrainMesh.triangles = terrainTriangles;
        terrainMesh.uv = terrainUVs;
        terrainMesh.colors = terrainColors;
        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();
    }

    Vector2[] CalcTerrainUVs(Vector2[] terrainUVs) {
        for (int index = 0, z = 0; z < meshVertexDepth-1; z++) {
            for (int x = 0; x < meshVertexLenght-1; x++) {
                terrainUVs[index * 4 + 0] = new Vector2((float)x/(meshVertexLenght-1), (float)z/(meshVertexDepth-1));
                terrainUVs[index * 4 + 1] = new Vector2((float)x/(meshVertexLenght-1), (float)(z+1)/(meshVertexDepth-1));
                terrainUVs[index * 4 + 2] = new Vector2((float)(x+1)/(meshVertexLenght-1), (float)(z+1)/(meshVertexDepth-1));
                terrainUVs[index * 4 + 3] = new Vector2((float)(x+1)/(meshVertexLenght-1), (float)z/(meshVertexDepth-1));
                index++;
            }
        }
        
        return terrainUVs;
    }

    Color[] CalcTerrainColors(Color[] terrainColors, Vector3[] terrainVertices) {
        if (lockGradientOnCurrentHeights) {
            gradientMinValue = minTerrainHeight;
            gradientMaxValue = maxTerrainHeight;
            lockGradientOnCurrentHeights = false;
        }

        bool noTextureAvailable = !biomeGenerator.isBiomeTextureGenerated();
        for (int i = 0, z = 0; z < meshVertexDepth-1; z++) {
            for (int x = 0; x < meshVertexLenght-1; x++) {
                terrainColors[i * 4 + 0] = calcVertexColor(i * 4 + 0, x, z, currentColorMapID);
                terrainColors[i * 4 + 1] = calcVertexColor(i * 4 + 1, x, z, currentColorMapID);
                terrainColors[i * 4 + 2] = calcVertexColor(i * 4 + 2, x, z, currentColorMapID);
                terrainColors[i * 4 + 3] = calcVertexColor(i * 4 + 3, x, z, currentColorMapID);
                i++;
            }
        }

        return terrainColors;

        Color calcVertexColor(int vertexIndex, int xCoord, int zCoord, int currentColorMapID) {
            if(currentColorMapID == gradientMapID || noTextureAvailable) {
                float gradientValue = Mathf.InverseLerp(gradientMinValue, gradientMaxValue, terrainVertices[vertexIndex].y);
                return usedGradient.Evaluate(gradientValue);
            } else if(currentColorMapID == biomeMapID) {
                return terrainColors[vertexIndex] = biomeGenerator.getBiomeTexture().GetPixel(xCoord,zCoord);
            } else if(currentColorMapID == temperatureMapID) {
                return terrainColors[vertexIndex] = biomeGenerator.tempTexture.GetPixel(xCoord,zCoord);
            } else if(currentColorMapID == precipitationMapID) {
                return terrainColors[vertexIndex] = biomeGenerator.precTexture.GetPixel(xCoord,zCoord);
            }
            return Color.white;
        }
    }

    int[] CalcTerrainTriangles(int[] terrainTriangles) {
        int currentVertex = 0;
        int currentTriangles = 0;
        for (int z = 0; z < meshVertexDepth-1; z++) {
            for (int x = 0; x < meshVertexLenght-1; x++) {
                terrainTriangles[0 + currentTriangles] = 4 * currentVertex + 0;
                terrainTriangles[1 + currentTriangles] = 4 * currentVertex + 1;
                terrainTriangles[2 + currentTriangles] = 4 * currentVertex + 2;
                terrainTriangles[3 + currentTriangles] = 4 * currentVertex + 0;
                terrainTriangles[4 + currentTriangles] = 4 * currentVertex + 2;
                terrainTriangles[5 + currentTriangles] = 4 * currentVertex + 3;

                currentVertex++;
                currentTriangles += 6;
            }
        }
        return terrainTriangles;
    }

    Vector3[] CalcTerrainVertices(Vector3[] terrainVertices) {
        
        for (int nodeIndex = 0, z = 0; z < terrainDepth+3; z++) {
            for (int x = 0; x < terrainLenght+3; x++) {
                float xCoord = x * terrainScaling;
                float zCoord = z * terrainScaling;
                uncondensedTerrainVertices[nodeIndex] = new Vector3(xCoord, CalcVertexHeight(x, z), zCoord);

                //track min/max terrain height
                float vertexHeight = uncondensedTerrainVertices[nodeIndex].y;
                if(x>0 && z>0 && x<terrainLenght+3 && z<terrainDepth+3) {
                    if (x==1 && z==1) {
                        minTerrainHeight = vertexHeight;
                        maxTerrainHeight = vertexHeight;
                    } else {
                        if (vertexHeight < minTerrainHeight)
                            minTerrainHeight = vertexHeight;
                        if (vertexHeight > maxTerrainHeight)
                            maxTerrainHeight = vertexHeight;
                    }
                }

                nodeIndex++;
            }
        }

        for (int nodeIndex = 0, z = 0; z < meshVertexDepth-1; z++) {
            for (int x = 0; x < meshVertexLenght-1; x++) {
                terrainVertices[nodeIndex * 4 + 0] = calcVertexCoords(x,z);
                terrainVertices[nodeIndex * 4 + 1] = calcVertexCoords(x,z+1);
                terrainVertices[nodeIndex * 4 + 2] = calcVertexCoords(x+1,z+1);
                terrainVertices[nodeIndex * 4 + 3] = calcVertexCoords(x+1,z);
                nodeIndex++;
            }
        }

        //calculate vertex world coordinates with edge cases and vertex density
        Vector3 calcVertexCoords(int x, int z) {
            float vertexCoordX;
            float vertexCoordZ;
            float heightCoordX = x;
            float heightCoordZ = z;
            if(x==0 || x==meshVertexLenght-1) {
                if(z==0 || z==meshVertexDepth-1) {
                    vertexCoordX = (x/(meshVertexLenght-1)) * (terrainLenght+2) * terrainScaling;
                    vertexCoordZ = (z/(meshVertexDepth-1)) * (terrainDepth+2) * terrainScaling;
                } else {
                    vertexCoordX = (x/(meshVertexLenght-1)) * (terrainLenght+2) * terrainScaling;
                    vertexCoordZ = terrainScaling + (z-1)*(terrainScaling/(float) vertexDensity);
                    heightCoordZ = 1 + z*(1f/(float) vertexDensity);
                }
            } else if(z==0 || z==meshVertexDepth-1) {
                if(x==0 || x==meshVertexLenght-1) {
                    vertexCoordX = (x/(meshVertexLenght-1)) * (terrainLenght+2) * terrainScaling;
                    vertexCoordZ = (z/(meshVertexDepth-1)) * (terrainDepth+2) * terrainScaling;
                } else {
                    vertexCoordX = terrainScaling + (x-1)*(terrainScaling/(float) vertexDensity);
                    vertexCoordZ = (z/(meshVertexDepth-1)) * (terrainDepth+2) * terrainScaling;
                    heightCoordX = 1 + x*(1f/(float) vertexDensity);
                }
            } else {
                vertexCoordX = terrainScaling + (x-1)*(terrainScaling/(float) vertexDensity);
                vertexCoordZ = terrainScaling + (z-1)*(terrainScaling/(float) vertexDensity);
                heightCoordX = 1 + (x-1)*(1f/(float) vertexDensity);
                heightCoordZ = 1 + (z-1)*(1f/(float) vertexDensity);
            }
            return new Vector3(vertexCoordX, CalcVertexHeight(heightCoordX, heightCoordZ), vertexCoordZ);
        }

        float CalcVertexHeight(float heightCoordX, float heightCoordZ) {
            //base noise values for height calculations
            float noiseXCoord = heightCoordX * heightDensity + noiseOffsetX;
            float noiseYCoord = heightCoordZ * heightDensity + noiseOffsetZ;
            float vertexHeight = Mathf.PerlinNoise(noiseXCoord, noiseYCoord);

            //calculate breakpoint where upper height area begins (upper X percent of terrain)
            float heightDifference = maxTerrainHeight - minTerrainHeight;
            float upperHeightRange = heightDifference * upperHeightPercent;
            float upperHeightBegin = minTerrainHeight + (heightDifference - upperHeightRange);

            //terrain roughness for upper and lower height areas
            float currentWorldHeight = Mathf.Lerp(minTerrainHeight, maxTerrainHeight, vertexHeight);
            float usedHeightVariety;
            if (currentWorldHeight < upperHeightBegin) {
                //blend layers for a smooth border
                if(noiseLayerBlendingArea > 0) {
                    float lowerLerpBegin = upperHeightBegin - (heightDifference * noiseLayerBlendingArea);
                    float lerpValue = Mathf.InverseLerp(lowerLerpBegin, upperHeightBegin, currentWorldHeight);
                    usedHeightVariety = Mathf.Lerp(lowerNoiseLayerHeightVariety, 0, lerpValue);
                } else {
                    usedHeightVariety = lowerNoiseLayerHeightVariety;
                }

                vertexHeight = AddNoiseLayers(vertexHeight, usedHeightVariety, lowerNoiseLayerDensity, heightCoordX, heightCoordZ);
            } else {
                //blend layers for a smooth border
                if(noiseLayerBlendingArea > 0) {
                    float upperLerpEnd = upperHeightBegin + (heightDifference * noiseLayerBlendingArea);
                    float lerpValue = Mathf.InverseLerp(upperHeightBegin, upperLerpEnd, currentWorldHeight);
                    usedHeightVariety = Mathf.Lerp(0, upperNoiseLayerHeightVariety, lerpValue);
                } else {
                    usedHeightVariety = upperNoiseLayerHeightVariety;
                }

                vertexHeight = AddNoiseLayers(vertexHeight, usedHeightVariety, upperNoiseLayerDensity, heightCoordX, heightCoordZ);
            }

            //noise layers can rarely lower the terrain into negative domain
            //clamp that to 0 to prevent imaginary numbers in the powering step
            vertexHeight = Mathf.Max(0,vertexHeight);
            
            //terrain height additions/multiplications/powering
            vertexHeight = Mathf.Pow(vertexHeight, noisePower);
            vertexHeight *= heightVariety;
            vertexHeight *= terrainScaling;
            return vertexHeight;
        }

        float AddNoiseLayers(float height, float noiseLayerHeightVariety, float noiseLayerDensity, float xCoord, float zCoord)
        {
            height += noiseLayerHeightVariety *
                Mathf.PerlinNoise(
                    xCoord * noiseLayerDensity + noiseOffsetX + 5.3f,
                    zCoord * noiseLayerDensity + noiseOffsetZ + 9.1f
                );
            height += noiseLayerHeightVariety / 2 *
                Mathf.PerlinNoise(
                    xCoord * noiseLayerDensity*2 + noiseOffsetX + 17.8f,
                    zCoord * noiseLayerDensity*2 + noiseOffsetZ + 23.5f
                );
            height /= (1 + noiseLayerHeightVariety + noiseLayerHeightVariety/2);
            return height;
        }

        //close the edges of the terrain
        for (int i = 0; i < terrainVertices.Length; i++)
        {
            float x = terrainVertices[i].x;
            float z = terrainVertices[i].z;
            if (x == 0) {
                terrainVertices[i] = new Vector3(x+terrainScaling, minTerrainHeight, z);
            }
            if (Mathf.Round(x) == Mathf.Round((terrainLenght+2)*terrainScaling)) {
                terrainVertices[i] = new Vector3(x-terrainScaling, minTerrainHeight, z);
            }
            if (z == 0) {
                terrainVertices[i] = new Vector3(terrainVertices[i].x, minTerrainHeight, z+terrainScaling);
            }
            if (Mathf.Round(z) == Mathf.Round((terrainDepth+2)*terrainScaling)) {
                terrainVertices[i] = new Vector3(terrainVertices[i].x, minTerrainHeight, z-terrainScaling);
            }
        }

        return terrainVertices;
    }

    void SetPredefinedParameters() {
        if (generateMountains) {
            currentLandscapeID = mountainsID;
            SetMountainsSettings();
            generateMountains = false;
        }
        if (generateIslands) {
            currentLandscapeID = islandsID;
            SetIslandsSettings();
            generateIslands = false;
        }

        void SetMountainsSettings() {
            heightDensity = 0.045f;
            heightVariety = 25f;
            noisePower = 2f;
            lowerNoiseLayerHeightVariety = 0.15f;
            lowerNoiseLayerDensity = 0.1f;
            upperHeightPercent = 0.5f;
            upperNoiseLayerHeightVariety = 0.2f;
            upperNoiseLayerDensity = 0.2f;
            relativeWaterHeight = 0.09f;
            usedGradient = mountainGradient;
            lockWaterPlaneOnCurrentHeights = true;
            lockGradientOnCurrentHeights = true;
        }

        void SetIslandsSettings() {
            heightDensity = 0.035f;
            heightVariety = 10;
            noisePower = 1.5f;
            lowerNoiseLayerHeightVariety = 0.2f;
            lowerNoiseLayerDensity = 0.15f;
            upperHeightPercent = 0.341f;
            upperNoiseLayerHeightVariety = 0.2f;
            upperNoiseLayerDensity = 0.3f;
            relativeWaterHeight = 0.434f;
            usedGradient = islandGradient;
            lockWaterPlaneOnCurrentHeights = true;
            lockGradientOnCurrentHeights = true;
        }
    }

    void UpdateColorMap() {
        if(colorView != currentColorView) {
			currentColorView = colorView;
			switch (colorView) {
				case ColorView.Gradient:
                    currentColorMapID = gradientMapID;
					break;
				case ColorView.Biome:
                    currentColorMapID = biomeMapID;
					break;
                case ColorView.Temp:
                    currentColorMapID = temperatureMapID;
					break;
                case ColorView.Prec:
                    currentColorMapID = precipitationMapID;
					break;
			}
		}
    }

    public float getMinTerrainheight() {
        return minTerrainHeight;
    }

    public float getMaxTerrainheight() {
        return maxTerrainHeight;
    }

    public Vector3[] getUncondensedTerrainVertices() {
        return uncondensedTerrainVertices;
    }

    public Vector2Int getMeshSize() {
        return new Vector2Int(meshVertexLenght, meshVertexDepth);
    }

    public Vector2Int getUncondensedMeshSize() {
        return new Vector2Int(terrainLenght+3, terrainDepth+3);
    }

    public bool isMeshGenerated() {
        return meshGenerated;
    }
}