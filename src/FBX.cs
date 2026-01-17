using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using Fbx;

namespace TS4SimRipper
{
    public class FBX
    {
        private List<FbxMesh> meshes;
        private FbxSkeleton skeleton;
        private bool y_up;

        private string exportDirectory;
        private string exportBasename;
        private List<ExportTexture> exportTextures;

        private const float SkeletonDiagZeroTranslationEpsilon = 1e-4f;

        public FbxMesh[] Meshes
        {
            get { return this.meshes.ToArray(); }
            set { this.meshes = new List<FbxMesh>(value); }
        }

        public FBX(GEOM[] geomArray, string[] meshNames, RIG rig, bool Y_Up, bool clean, ref string errorMsg)
        {
            this.y_up = Y_Up;
            this.meshes = new List<FbxMesh>();
            Matrix4D axisTransform = Y_Up ? Matrix4D.Identity : Matrix4D.RotateYupToZup;

            // Convert GEOM data to FBX mesh structures
            for (int m = 0; m < geomArray.Length; m++)
            {
                try
                {
                    GEOM geom = new GEOM(geomArray[m]);
                    GEOM.GeometryState geostate = geom.GeometryStates.FirstOrDefault() ?? geom.FullMeshGeometryState();
                    geom.FixUnusedBones();

                    string meshName = meshNames != null && m < meshNames.Length ? meshNames[m].Replace(" ", "") : $"Mesh_{m}";

                    FbxMesh fbxMesh = new FbxMesh();
                    fbxMesh.meshName = meshName;
                    fbxMesh.meshID = $"Geometry::{meshName}";
                    fbxMesh.shader = geom.ShaderHash;

                    // Extract vertex data from GEOM
                    List<Vector3> positions = new List<Vector3>();
                    List<Vector3> normals = new List<Vector3>();
                    List<Vector2> uvList = new List<Vector2>();

                    bool hasNormals = geom.hasNormals;
                    bool hasUVs = geom.hasUVs;

                    for (int v = geostate.MinVertexIndex; v < geostate.VertexCount; v++)
                    {
                        // Transform vertices by axisTransform to match coordinate system
                        // This ensures vertices and bones are in the same space for skinning
                        positions.Add(axisTransform * new Vector3(geom.getPosition(v)));
                        if (hasNormals) normals.Add(axisTransform * new Vector3(geom.getNormal(v)));
                        if (hasUVs)
                        {
                            var uv = geom.getUV(v, 0);
                            // Flip V coordinate (Y-axis) to correct upside-down UVs in FBX
                            uvList.Add(new Vector2(uv[0], 1.0f - uv[1]));
                        }
                    }

                    fbxMesh.positions = positions.ToArray();
                    fbxMesh.normals = normals.ToArray();
                    fbxMesh.uvs = uvList.ToArray();

                    // Extract face indices
                    List<uint> faceIndices = new List<uint>();
                    for (int f = geostate.StartFace; f < geostate.PrimitiveCount; f++)
                    {
                        uint[] face = geom.getFaceIndicesUint(f);
                        faceIndices.AddRange(face);
                    }
                    fbxMesh.faceIndices = faceIndices.ToArray();

                    // Extract bone weights if rig is provided
                    if (rig != null)
                    {
                        fbxMesh.boneWeights = ExtractBoneWeights(geom, rig, geostate);
                    }

                    meshes.Add(fbxMesh);
                }
                catch (Exception ex)
                {
                    errorMsg += $"Error processing mesh {m}: {ex.Message}\n";
                }
            }

            // Build skeleton from RIG
            if (rig != null)
            {
                skeleton = BuildSkeleton(rig);
                AppendSkeletonDiagnostics(rig, ref errorMsg);
            }
        }

        private void AppendSkeletonDiagnostics(RIG rig, ref string errorMsg)
        {
            try
            {
                if (skeleton == null || skeleton.bones == null || skeleton.bones.Count == 0)
                    return;

                int boneCount = skeleton.bones.Count;
                int zeroTranslations = 0;
                float maxTranslationMag = 0f;
                string maxTranslationName = "";

                double maxGlobalRecomposeAbsDiff = 0.0;
                string maxGlobalRecomposeName = "";

                for (int i = 0; i < boneCount; i++)
                {
                    var b = skeleton.bones[i];
                    float tMag = b.position.Magnitude;
                    if (tMag < SkeletonDiagZeroTranslationEpsilon)
                        zeroTranslations++;

                    if (tMag > maxTranslationMag)
                    {
                        maxTranslationMag = tMag;
                        maxTranslationName = b.name;
                    }

                    // Sanity: globals reconstructed from locals should match exported globals.
                    Matrix4D parentGlobal = Matrix4D.Identity;
                    if (b.parentIndex >= 0 && b.parentIndex < boneCount)
                        parentGlobal = skeleton.bones[b.parentIndex].globalTransform;

                    Matrix4D recomposed = parentGlobal * b.localTransform;
                    var aVals = recomposed.Values;
                    var bVals = b.globalTransform.Values;
                    for (int k = 0; k < 16; k++)
                    {
                        double d = Math.Abs(aVals[k] - bVals[k]);
                        if (d > maxGlobalRecomposeAbsDiff)
                        {
                            maxGlobalRecomposeAbsDiff = d;
                            maxGlobalRecomposeName = b.name;
                        }
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("[FBX Export Diagnostics]");
                sb.AppendLine($"Bones: {boneCount}");
                sb.AppendLine($"Local translations near-zero (< {SkeletonDiagZeroTranslationEpsilon.ToString(CultureInfo.InvariantCulture)}): {zeroTranslations} ({(100.0 * zeroTranslations / Math.Max(1, boneCount)).ToString("F1", CultureInfo.InvariantCulture)}%)");
                sb.AppendLine($"Max local translation magnitude: {maxTranslationMag.ToString("G7", CultureInfo.InvariantCulture)} (bone '{maxTranslationName}')");
                sb.AppendLine($"Max |(parentGlobal*local) - global| element diff: {maxGlobalRecomposeAbsDiff.ToString("G7", CultureInfo.InvariantCulture)} (bone '{maxGlobalRecomposeName}')");

                // Show a few key bones if present.
                string[] keyNames = new[] { "b__ROOT__", "b__ROOT_bind__", "b__Pelvis__", "b__Spine0__", "b__Spine1__" };
                foreach (string key in keyNames)
                {
                    int idx = skeleton.bones.FindIndex(x => string.Equals(x.name, key, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0)
                        continue;
                    var b = skeleton.bones[idx];
                    sb.AppendLine($"{b.name}: parent={b.parentIndex}, lclT=({b.position.X.ToString("G7", CultureInfo.InvariantCulture)}, {b.position.Y.ToString("G7", CultureInfo.InvariantCulture)}, {b.position.Z.ToString("G7", CultureInfo.InvariantCulture)})");
                }

                // Definitions/units sanity (helps catch importer issues unrelated to bone math)
                int meshCount = meshes?.Count ?? 0;
                int skinnedMeshCount = 0;
                int clusterCount = 0;
                if (meshCount > 0)
                {
                    foreach (var mesh in meshes)
                    {
                        if (mesh?.boneWeights != null && mesh.boneWeights.Count > 0)
                        {
                            skinnedMeshCount++;
                            clusterCount += new HashSet<int>(mesh.boneWeights.Select(w => w.boneIndex)).Count;
                        }
                    }
                }

                int globalSettingsCount = 1;
                int nodeAttributeCount = boneCount;
                int modelCount = meshCount + boneCount;
                int geometryCount = meshCount;
                int deformerCount = skinnedMeshCount + clusterCount;
                int materialCount = meshCount;
                int poseCount = (boneCount > 0) ? 1 : 0;
                int totalDefinitionsCount = globalSettingsCount + nodeAttributeCount + modelCount + geometryCount + deformerCount + materialCount + poseCount;

                sb.AppendLine();
                sb.AppendLine("[FBX Definitions Diagnostics]");
                sb.AppendLine("UnitScaleFactor: 100.0");
                sb.AppendLine("OriginalUnitScaleFactor: 100.0");
                sb.AppendLine($"Definitions.Count (total objects): {totalDefinitionsCount}");
                sb.AppendLine($"Counts: GlobalSettings={globalSettingsCount}, NodeAttribute={nodeAttributeCount}, Model={modelCount}, Geometry={geometryCount}, Deformer={deformerCount} (skins={skinnedMeshCount}, clusters={clusterCount}), Material={materialCount}, Pose={poseCount}");

                errorMsg += sb.ToString();
            }
            catch
            {
                // Never block export on diagnostics.
            }
        }

        private List<BoneWeight> ExtractBoneWeights(GEOM geom, RIG rig, GEOM.GeometryState geostate)
        {
            var weights = new List<BoneWeight>();

            // Check if mesh has bone data
            if (geom.BoneHashList == null || geom.BoneHashList.Length == 0)
                return weights;

            // Create mapping from bone hash to skeleton bone index
            Dictionary<uint, int> boneHashToIndex = new Dictionary<uint, int>();
            for (int i = 0; i < rig.NumberBones; i++)
            {
                boneHashToIndex[rig.Bones[i].BoneHash] = i;
            }

            // Extract bone weights for each vertex
            for (int v = geostate.MinVertexIndex; v < geostate.VertexCount; v++)
            {
                byte[] boneAssignments = geom.getBones(v);
                byte[] boneWeightBytes = geom.getBoneWeights(v);

                // Process up to 4 bone influences per vertex
                for (int b = 0; b < 4; b++)
                {
                    byte meshBoneIndex = boneAssignments[b];
                    byte weightByte = boneWeightBytes[b];

                    // Skip zero weights
                    if (weightByte == 0)
                        continue;

                    // Get the bone hash from the mesh's bone list
                    if (meshBoneIndex >= geom.BoneHashList.Length)
                        continue;

                    uint boneHash = geom.BoneHashList[meshBoneIndex];

                    // Find the skeleton bone index
                    if (!boneHashToIndex.ContainsKey(boneHash))
                        continue;

                    int skeletonBoneIndex = boneHashToIndex[boneHash];

                    // Convert byte weight (0-255) to float (0.0-1.0)
                    float weight = weightByte / 255.0f;

                    weights.Add(new BoneWeight
                    {
                        vertexIndex = v - geostate.MinVertexIndex, // Relative to this mesh
                        boneIndex = skeletonBoneIndex,
                        weight = weight
                    });
                }
            }

            return weights;
        }

        private FbxSkeleton BuildSkeleton(RIG rig)
        {
            var skel = new FbxSkeleton();

            // Create axis transform matrix for coordinate system conversion if needed
            Matrix4D axisTransform = y_up ? Matrix4D.Identity : Matrix4D.RotateYupToZup;

            // IMPORTANT:
            // The skin clusters/bind pose use bone global matrices. Blender also reconstructs
            // bone globals from the exported local TRS hierarchy. If those disagree, meshes
            // will "explode" or the rest pose can collapse.
            //
            // To keep them consistent, compute local transforms from the converted globals:
            //   global' = axisTransform * global
            //   local'  = inverse(parentGlobal') * global'
            // Then decompose local' into translation + rotation for the FBX Model node.

            static Quaternion QuaternionFromRotationMatrix(double[,] m)
            {
                // Assumes m is a 3x3 orthonormal rotation matrix.
                double trace = m[0, 0] + m[1, 1] + m[2, 2];
                double x, y, z, w;
                if (trace > 0.0)
                {
                    double s = Math.Sqrt(trace + 1.0) * 2.0;
                    w = 0.25 * s;
                    x = (m[2, 1] - m[1, 2]) / s;
                    y = (m[0, 2] - m[2, 0]) / s;
                    z = (m[1, 0] - m[0, 1]) / s;
                }
                else if (m[0, 0] > m[1, 1] && m[0, 0] > m[2, 2])
                {
                    double s = Math.Sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2.0;
                    w = (m[2, 1] - m[1, 2]) / s;
                    x = 0.25 * s;
                    y = (m[0, 1] + m[1, 0]) / s;
                    z = (m[0, 2] + m[2, 0]) / s;
                }
                else if (m[1, 1] > m[2, 2])
                {
                    double s = Math.Sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2.0;
                    w = (m[0, 2] - m[2, 0]) / s;
                    x = (m[0, 1] + m[1, 0]) / s;
                    y = 0.25 * s;
                    z = (m[1, 2] + m[2, 1]) / s;
                }
                else
                {
                    double s = Math.Sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2.0;
                    w = (m[1, 0] - m[0, 1]) / s;
                    x = (m[0, 2] + m[2, 0]) / s;
                    y = (m[1, 2] + m[2, 1]) / s;
                    z = 0.25 * s;
                }

                var q = new Quaternion(x, y, z, w);
                if (!q.isNormalized)
                    q.Normalize();
                return q;
            }

            // First, compute converted globals for every bone.
            var globals = new Matrix4D[rig.NumberBones];
            for (int i = 0; i < rig.NumberBones; i++)
            {
                globals[i] = axisTransform * rig.Bones[i].GlobalTransform;
            }

            // Then compute locals from globals and decompose.
            for (int i = 0; i < rig.NumberBones; i++)
            {
                RIG.Bone rigBone = rig.Bones[i];
                FbxBone fbxBone = new FbxBone();

                fbxBone.name = rigBone.BoneName;
                fbxBone.parentIndex = rigBone.ParentBoneIndex;

                Matrix4D local;
                if (rigBone.ParentBoneIndex >= 0 && rigBone.ParentBoneIndex < globals.Length)
                {
                    var parentGlobal = globals[rigBone.ParentBoneIndex];
                    local = parentGlobal.Inverse() * globals[i];
                }
                else
                {
                    local = globals[i];
                }

                // Translation comes from the matrix offset.
                fbxBone.position = local.Offset;

                // Build a pure rotation matrix by removing translation and normalizing the
                // first three columns (in case of tiny scale drift).
                var m = local.Matrix;
                m[0, 3] = m[1, 3] = m[2, 3] = 0.0;
                m[3, 0] = m[3, 1] = m[3, 2] = 0.0;
                m[3, 3] = 1.0;

                double sx = Math.Sqrt(m[0, 0] * m[0, 0] + m[1, 0] * m[1, 0] + m[2, 0] * m[2, 0]);
                double sy = Math.Sqrt(m[0, 1] * m[0, 1] + m[1, 1] * m[1, 1] + m[2, 1] * m[2, 1]);
                double sz = Math.Sqrt(m[0, 2] * m[0, 2] + m[1, 2] * m[1, 2] + m[2, 2] * m[2, 2]);
                if (sx == 0) sx = 1;
                if (sy == 0) sy = 1;
                if (sz == 0) sz = 1;
                m[0, 0] /= sx; m[1, 0] /= sx; m[2, 0] /= sx;
                m[0, 1] /= sy; m[1, 1] /= sy; m[2, 1] /= sy;
                m[0, 2] /= sz; m[1, 2] /= sz; m[2, 2] /= sz;

                fbxBone.rotation = QuaternionFromRotationMatrix(m);

                // Bones are exported with unit scaling in FBX node properties.
                fbxBone.scale = rigBone.ScalingVector;
                fbxBone.localTransform = local;
                fbxBone.globalTransform = globals[i];

                skel.bones.Add(fbxBone);
            }

            return skel;
        }

        public void Write(string path, bool flipYZ, float boneDivider, bool linkTextures, bool glassTextures, bool wingTextures)
        {
            exportDirectory = Path.GetDirectoryName(path);
            exportBasename = Path.GetFileNameWithoutExtension(path).Replace(" ", "_");
            exportTextures = PrepareExportTextures(exportDirectory, exportBasename, linkTextures, glassTextures, wingTextures);

            var doc = new FbxDocument();
            doc.Version = FbxVersion.v7_4;

            // Add header extension
            var headerExt = CreateHeaderExtension();
            doc.Nodes.Add(headerExt);

            // Add global settings
            var globalSettings = CreateGlobalSettings();
            doc.Nodes.Add(globalSettings);

            // Add documents description
            var documents = CreateDocuments();
            doc.Nodes.Add(documents);

            // Add references
            var references = new FbxNode { Name = "References" };
            doc.Nodes.Add(references);

            // Add definitions
            var definitions = CreateDefinitions(linkTextures, glassTextures, wingTextures);
            doc.Nodes.Add(definitions);

            // Add objects (meshes, materials, etc.)
            var objects = CreateObjects(linkTextures, glassTextures, wingTextures);
            doc.Nodes.Add(objects);

            // Add connections
            var connections = CreateConnections(linkTextures, glassTextures, wingTextures);
            doc.Nodes.Add(connections);

            // Write to file - FbxIO.WriteAscii creates its own FileStream
            FbxIO.WriteAscii(doc, path);
        }

        private FbxNode CreateHeaderExtension()
        {
            var header = new FbxNode { Name = "FBXHeaderExtension" };

            var version = new FbxNode { Name = "FBXHeaderVersion", Properties = { 1003 } };
            header.Nodes.Add(version);

            var fbxVersion = new FbxNode { Name = "FBXVersion", Properties = { 7400 } };
            header.Nodes.Add(fbxVersion);

            var encryptionType = new FbxNode { Name = "EncryptionType", Properties = { 0 } };
            header.Nodes.Add(encryptionType);

            var creationTimeStamp = new FbxNode { Name = "CreationTimeStamp" };
            var now = DateTime.Now;
            creationTimeStamp.Nodes.Add(new FbxNode { Name = "Version", Properties = { 1000 } });
            creationTimeStamp.Nodes.Add(new FbxNode { Name = "Year", Properties = { now.Year } });
            creationTimeStamp.Nodes.Add(new FbxNode { Name = "Month", Properties = { now.Month } });
            creationTimeStamp.Nodes.Add(new FbxNode { Name = "Day", Properties = { now.Day } });
            creationTimeStamp.Nodes.Add(new FbxNode { Name = "Hour", Properties = { now.Hour } });
            creationTimeStamp.Nodes.Add(new FbxNode { Name = "Minute", Properties = { now.Minute } });
            creationTimeStamp.Nodes.Add(new FbxNode { Name = "Second", Properties = { now.Second } });
            creationTimeStamp.Nodes.Add(new FbxNode { Name = "Millisecond", Properties = { now.Millisecond } });
            header.Nodes.Add(creationTimeStamp);

            var creator = new FbxNode { Name = "Creator", Properties = { "TS4 SimRipper FBX Exporter" } };
            header.Nodes.Add(creator);

            return header;
        }

        private FbxNode CreateGlobalSettings()
        {
            var settings = new FbxNode { Name = "GlobalSettings" };

            var version = new FbxNode { Name = "Version", Properties = { 1000 } };
            settings.Nodes.Add(version);

            var properties70 = new FbxNode { Name = "Properties70" };

            // Up axis
            var upAxis = new FbxNode { Name = "P" };
            upAxis.Properties.Add("UpAxis");
            upAxis.Properties.Add("int");
            upAxis.Properties.Add("Integer");
            upAxis.Properties.Add("");
            upAxis.Properties.Add(y_up ? 1 : 2);
            properties70.Nodes.Add(upAxis);

            // Up axis sign
            var upAxisSign = new FbxNode { Name = "P" };
            upAxisSign.Properties.Add("UpAxisSign");
            upAxisSign.Properties.Add("int");
            upAxisSign.Properties.Add("Integer");
            upAxisSign.Properties.Add("");
            upAxisSign.Properties.Add(1);
            properties70.Nodes.Add(upAxisSign);

            // Front axis
            var frontAxis = new FbxNode { Name = "P" };
            frontAxis.Properties.Add("FrontAxis");
            frontAxis.Properties.Add("int");
            frontAxis.Properties.Add("Integer");
            frontAxis.Properties.Add("");
            frontAxis.Properties.Add(2);
            properties70.Nodes.Add(frontAxis);

            // Front axis sign
            var frontAxisSign = new FbxNode { Name = "P" };
            frontAxisSign.Properties.Add("FrontAxisSign");
            frontAxisSign.Properties.Add("int");
            frontAxisSign.Properties.Add("Integer");
            frontAxisSign.Properties.Add("");
            frontAxisSign.Properties.Add(1);
            properties70.Nodes.Add(frontAxisSign);

            // Coord axis
            var coordAxis = new FbxNode { Name = "P" };
            coordAxis.Properties.Add("CoordAxis");
            coordAxis.Properties.Add("int");
            coordAxis.Properties.Add("Integer");
            coordAxis.Properties.Add("");
            coordAxis.Properties.Add(0);
            properties70.Nodes.Add(coordAxis);

            // Coord axis sign
            var coordAxisSign = new FbxNode { Name = "P" };
            coordAxisSign.Properties.Add("CoordAxisSign");
            coordAxisSign.Properties.Add("int");
            coordAxisSign.Properties.Add("Integer");
            coordAxisSign.Properties.Add("");
            coordAxisSign.Properties.Add(1);
            properties70.Nodes.Add(coordAxisSign);

            // Unit scale - set to meters (100 cm per unit) for Blender-friendly size
            var unitScale = new FbxNode { Name = "P" };
            unitScale.Properties.Add("UnitScaleFactor");
            unitScale.Properties.Add("double");
            unitScale.Properties.Add("Number");
            unitScale.Properties.Add("");
            unitScale.Properties.Add(100.0);
            properties70.Nodes.Add(unitScale);

            // Original unit scale factor
            var originalUnitScale = new FbxNode { Name = "P" };
            originalUnitScale.Properties.Add("OriginalUnitScaleFactor");
            originalUnitScale.Properties.Add("double");
            originalUnitScale.Properties.Add("Number");
            originalUnitScale.Properties.Add("");
            originalUnitScale.Properties.Add(100.0);
            properties70.Nodes.Add(originalUnitScale);

            settings.Nodes.Add(properties70);
            return settings;
        }

        private FbxNode CreateDocuments()
        {
            var documents = new FbxNode { Name = "Documents" };

            var count = new FbxNode { Name = "Count", Properties = { 1 } };
            documents.Nodes.Add(count);

            var document = new FbxNode { Name = "Document" };
            document.Properties.Add(1234567890L);
            document.Properties.Add("");
            document.Properties.Add("Scene");

            var props70 = new FbxNode { Name = "Properties70" };
            var sourceObject = new FbxNode { Name = "P" };
            sourceObject.Properties.Add("SourceObject");
            sourceObject.Properties.Add("object");
            sourceObject.Properties.Add("");
            sourceObject.Properties.Add("");
            props70.Nodes.Add(sourceObject);

            var activeAnimStackName = new FbxNode { Name = "P" };
            activeAnimStackName.Properties.Add("ActiveAnimStackName");
            activeAnimStackName.Properties.Add("KString");
            activeAnimStackName.Properties.Add("");
            activeAnimStackName.Properties.Add("");
            activeAnimStackName.Properties.Add("");
            props70.Nodes.Add(activeAnimStackName);

            document.Nodes.Add(props70);
            document.Nodes.Add(new FbxNode { Name = "RootNode", Properties = { 0L } });

            documents.Nodes.Add(document);
            return documents;
        }

        private FbxNode CreateDefinitions(bool linkTextures, bool glassTextures, bool wingTextures)
        {
            var definitions = new FbxNode { Name = "Definitions" };

            var version = new FbxNode { Name = "Version", Properties = { 100 } };
            definitions.Nodes.Add(version);

            int boneCount = skeleton != null ? skeleton.bones.Count : 0;
            int meshCount = meshes.Count;

            int skinnedMeshCount = 0;
            int clusterCount = 0;
            if (boneCount > 0)
            {
                foreach (var mesh in meshes)
                {
                    if (mesh.boneWeights != null && mesh.boneWeights.Count > 0)
                    {
                        skinnedMeshCount++;
                        clusterCount += new HashSet<int>(mesh.boneWeights.Select(w => w.boneIndex)).Count;
                    }
                }
            }

            int globalSettingsCount = 1;
            int nodeAttributeCount = boneCount;
            int modelCount = meshCount + boneCount;
            int geometryCount = meshCount;
            int deformerCount = (boneCount > 0) ? (skinnedMeshCount + clusterCount) : 0;
            int materialCount = meshCount;
            int poseCount = (boneCount > 0) ? 1 : 0;

            int textureCount = 0;
            int videoCount = 0;
            if (linkTextures && exportTextures != null && exportTextures.Count > 0)
            {
                textureCount = exportTextures.Count;
                videoCount = exportTextures.Count;
            }

            int totalObjectCount = globalSettingsCount + nodeAttributeCount + modelCount + geometryCount + deformerCount + materialCount + poseCount + textureCount + videoCount;
            definitions.Nodes.Add(new FbxNode { Name = "Count", Properties = { totalObjectCount } });

            // ObjectType: GlobalSettings
            var globalSettingsType = new FbxNode { Name = "ObjectType", Properties = { "GlobalSettings" } };
            globalSettingsType.Nodes.Add(new FbxNode { Name = "Count", Properties = { globalSettingsCount } });
            definitions.Nodes.Add(globalSettingsType);

            // ObjectType: NodeAttribute (bones)
            if (nodeAttributeCount > 0)
            {
                var nodeAttrType = new FbxNode { Name = "ObjectType", Properties = { "NodeAttribute" } };
                nodeAttrType.Nodes.Add(new FbxNode { Name = "Count", Properties = { nodeAttributeCount } });
                definitions.Nodes.Add(nodeAttrType);
            }

            // ObjectType: Model (meshes + bones)
            var modelType = new FbxNode { Name = "ObjectType", Properties = { "Model" } };
            modelType.Nodes.Add(new FbxNode { Name = "Count", Properties = { modelCount } });
            definitions.Nodes.Add(modelType);

            // ObjectType: Geometry
            var geometryType = new FbxNode { Name = "ObjectType", Properties = { "Geometry" } };
            geometryType.Nodes.Add(new FbxNode { Name = "Count", Properties = { geometryCount } });
            definitions.Nodes.Add(geometryType);

            // ObjectType: Deformer (skins + clusters)
            if (deformerCount > 0)
            {
                var deformerType = new FbxNode { Name = "ObjectType", Properties = { "Deformer" } };
                deformerType.Nodes.Add(new FbxNode { Name = "Count", Properties = { deformerCount } });
                definitions.Nodes.Add(deformerType);
            }

            // ObjectType: Material
            var materialType = new FbxNode { Name = "ObjectType", Properties = { "Material" } };
            materialType.Nodes.Add(new FbxNode { Name = "Count", Properties = { materialCount } });
            definitions.Nodes.Add(materialType);

            // ObjectType: Texture
            if (textureCount > 0)
            {
                var textureType = new FbxNode { Name = "ObjectType", Properties = { "Texture" } };
                textureType.Nodes.Add(new FbxNode { Name = "Count", Properties = { textureCount } });
                definitions.Nodes.Add(textureType);
            }

            // ObjectType: Video
            if (videoCount > 0)
            {
                var videoType = new FbxNode { Name = "ObjectType", Properties = { "Video" } };
                videoType.Nodes.Add(new FbxNode { Name = "Count", Properties = { videoCount } });
                definitions.Nodes.Add(videoType);
            }

            // ObjectType: Pose
            var poseType = new FbxNode { Name = "ObjectType", Properties = { "Pose" } };
            poseType.Nodes.Add(new FbxNode { Name = "Count", Properties = { poseCount } });
            definitions.Nodes.Add(poseType);

            return definitions;
        }

        private FbxNode CreateObjects(bool linkTextures, bool glassTextures, bool wingTextures)
        {
            var objects = new FbxNode { Name = "Objects" };

            long objectId = 1000000;
            long boneStartId = 2000000;
            long skinStartId = 3000000;
            long clusterStartId = 4000000;

            // Create skeleton bones first
            if (skeleton != null && skeleton.bones.Count > 0)
            {
                for (int i = 0; i < skeleton.bones.Count; i++)
                {
                    var bone = skeleton.bones[i];
                    long boneId = boneStartId + i;
                    long boneAttrId = boneStartId + skeleton.bones.Count + i;
                    bool isRootBone = (bone.parentIndex < 0); // Root bone has no parent

                    // Create bone model
                    var boneModel = CreateBoneModelNode(bone, boneId, isRootBone);
                    objects.Nodes.Add(boneModel);

                    // Create bone attribute (NodeAttribute)
                    var boneAttr = CreateBoneAttributeNode(bone, boneAttrId, i);
                    objects.Nodes.Add(boneAttr);
                }
            }

            // Create meshes with skin deformers
            foreach (var mesh in meshes)
            {
                long geometryId = objectId++;
                long modelId = objectId++;
                long materialId = objectId++;
                long skinId = skinStartId++;

                // Create Geometry
                var geometry = CreateGeometryNode(mesh, geometryId);
                objects.Nodes.Add(geometry);

                // Create Model
                var model = CreateModelNode(mesh, modelId);
                objects.Nodes.Add(model);

                // Create Material
                var material = CreateMaterialNode(mesh, materialId);
                objects.Nodes.Add(material);

                // Create skin deformer if mesh has bone weights
                if (skeleton != null && mesh.boneWeights != null && mesh.boneWeights.Count > 0)
                {
                    var skin = CreateSkinDeformerNode(skinId);
                    objects.Nodes.Add(skin);

                    // Create cluster (sub-deformer) for each bone affecting this mesh
                    var bonesInMesh = new HashSet<int>(mesh.boneWeights.Select(w => w.boneIndex));
                    foreach (int boneIndex in bonesInMesh)
                    {
                        long clusterId = clusterStartId++;
                        var cluster = CreateClusterNode(mesh, boneIndex, clusterId, boneStartId);
                        objects.Nodes.Add(cluster);
                    }
                }
            }

            // Create bind pose
            if (skeleton != null && skeleton.bones.Count > 0)
            {
                var bindPose = CreateBindPoseNode(boneStartId);
                objects.Nodes.Add(bindPose);
            }

            // Create texture/video objects so Blender can auto-load exported PNGs.
            if (linkTextures && exportTextures != null && exportTextures.Count > 0)
            {
                foreach (var tex in exportTextures)
                {
                    objects.Nodes.Add(CreateVideoNode(tex.VideoId, tex.VideoName, tex.FileName));
                    objects.Nodes.Add(CreateTextureNode(tex.TextureId, tex.TextureName, tex.FileName));
                }
            }

            return objects;
        }

        private FbxNode CreateVideoNode(long id, string name, string relativeFilename)
        {
            var video = new FbxNode { Name = "Video" };
            video.Properties.Add(id);
            video.Properties.Add($"Video::{name}");
            video.Properties.Add("Clip");

            video.Nodes.Add(new FbxNode { Name = "Type", Properties = { "Clip" } });
            video.Nodes.Add(new FbxNode { Name = "Properties70" });
            video.Nodes.Add(new FbxNode { Name = "FileName", Properties = { relativeFilename } });
            video.Nodes.Add(new FbxNode { Name = "RelativeFilename", Properties = { relativeFilename } });
            return video;
        }

        private FbxNode CreateTextureNode(long id, string name, string relativeFilename)
        {
            var texture = new FbxNode { Name = "Texture" };
            texture.Properties.Add(id);
            texture.Properties.Add($"Texture::{name}");
            texture.Properties.Add("TextureVideoClip");

            texture.Nodes.Add(new FbxNode { Name = "Type", Properties = { "TextureVideoClip" } });
            texture.Nodes.Add(new FbxNode { Name = "Version", Properties = { 202 } });

            var properties70 = new FbxNode { Name = "Properties70" };
            var uvSet = new FbxNode { Name = "P" };
            uvSet.Properties.Add("UVSet");
            uvSet.Properties.Add("KString");
            uvSet.Properties.Add("");
            uvSet.Properties.Add("");
            uvSet.Properties.Add("UVMap");
            properties70.Nodes.Add(uvSet);
            texture.Nodes.Add(properties70);

            texture.Nodes.Add(new FbxNode { Name = "FileName", Properties = { relativeFilename } });
            texture.Nodes.Add(new FbxNode { Name = "RelativeFilename", Properties = { relativeFilename } });
            return texture;
        }

        private FbxNode CreateBoneModelNode(FbxBone bone, long id, bool isRootBone)
        {
            var model = new FbxNode { Name = "Model" };
            model.Properties.Add(id);
            model.Properties.Add($"Model::{bone.name}");
            model.Properties.Add("LimbNode");

            var version = new FbxNode { Name = "Version", Properties = { 232 } };
            model.Nodes.Add(version);

            var properties70 = new FbxNode { Name = "Properties70" };

            // Translation - use the position vector directly
            var lcl_translation = new FbxNode { Name = "P" };
            lcl_translation.Properties.Add("Lcl Translation");
            lcl_translation.Properties.Add("Lcl Translation");
            lcl_translation.Properties.Add("");
            lcl_translation.Properties.Add("A");
            lcl_translation.Properties.Add((double)bone.position.X);
            lcl_translation.Properties.Add((double)bone.position.Y);
            lcl_translation.Properties.Add((double)bone.position.Z);
            properties70.Nodes.Add(lcl_translation);

            // Rotation - convert quaternion to Euler XYZ in degrees
            var euler = bone.rotation.toEuler();
            var lcl_rotation = new FbxNode { Name = "P" };
            lcl_rotation.Properties.Add("Lcl Rotation");
            lcl_rotation.Properties.Add("Lcl Rotation");
            lcl_rotation.Properties.Add("");
            lcl_rotation.Properties.Add("A");
            lcl_rotation.Properties.Add(euler.X * (180.0 / Math.PI));
            lcl_rotation.Properties.Add(euler.Y * (180.0 / Math.PI));
            lcl_rotation.Properties.Add(euler.Z * (180.0 / Math.PI));
            properties70.Nodes.Add(lcl_rotation);

            // Scale
            var lcl_scaling = new FbxNode { Name = "P" };
            lcl_scaling.Properties.Add("Lcl Scaling");
            lcl_scaling.Properties.Add("Lcl Scaling");
            lcl_scaling.Properties.Add("");
            lcl_scaling.Properties.Add("A");
            // Use unit scaling on bones to avoid chain collapse and importer scale drift
            lcl_scaling.Properties.Add(1.0);
            lcl_scaling.Properties.Add(1.0);
            lcl_scaling.Properties.Add(1.0);
            properties70.Nodes.Add(lcl_scaling);

            // Explicit rotation order (XYZ = 0)
            var rotOrder = new FbxNode { Name = "P" };
            rotOrder.Properties.Add("RotationOrder");
            rotOrder.Properties.Add("enum");
            rotOrder.Properties.Add("");
            rotOrder.Properties.Add("");
            rotOrder.Properties.Add(0);
            properties70.Nodes.Add(rotOrder);

            // Activate rotation with optional PreRotation to align axes without changing visible orientation
            var rotActive = new FbxNode { Name = "P" };
            rotActive.Properties.Add("RotationActive");
            rotActive.Properties.Add("bool");
            rotActive.Properties.Add("");
            rotActive.Properties.Add("");
            rotActive.Properties.Add(1);
            properties70.Nodes.Add(rotActive);

            // Enable Segment Scale Compensate to avoid scale propagation artifacts
            var ssc = new FbxNode { Name = "P" };
            ssc.Properties.Add("SegmentScaleCompensate");
            ssc.Properties.Add("bool");
            ssc.Properties.Add("");
            ssc.Properties.Add("");
            ssc.Properties.Add(1);
            properties70.Nodes.Add(ssc);

            // Set inherit type RSrs (1) to match SegmentScaleCompensate behaviour
            var inheritType = new FbxNode { Name = "P" };
            inheritType.Properties.Add("InheritType");
            inheritType.Properties.Add("enum");
            inheritType.Properties.Add("");
            inheritType.Properties.Add("");
            inheritType.Properties.Add(1);
            properties70.Nodes.Add(inheritType);

            model.Nodes.Add(properties70);

            return model;
        }

        private FbxNode CreateBoneAttributeNode(FbxBone bone, long id, int boneIndex)
        {
            var nodeAttr = new FbxNode { Name = "NodeAttribute" };
            nodeAttr.Properties.Add(id);
            nodeAttr.Properties.Add($"NodeAttribute::");
            nodeAttr.Properties.Add("LimbNode");

            var typeFlags = new FbxNode { Name = "TypeFlags", Properties = { "Skeleton" } };
            nodeAttr.Nodes.Add(typeFlags);

            // Blender uses Skeleton NodeAttribute properties like Size/LimbLength to initialize
            // display radius and bone length when importing FBX.
            double limbLength = 1.0;
            if (skeleton != null && boneIndex >= 0 && boneIndex < skeleton.bones.Count)
            {
                int childIndex = -1;
                for (int i = 0; i < skeleton.bones.Count; i++)
                {
                    if (skeleton.bones[i].parentIndex == boneIndex)
                    {
                        childIndex = i;
                        break;
                    }
                }

                if (childIndex >= 0)
                {
                    var head = skeleton.bones[boneIndex].globalTransform.Offset;
                    var tail = skeleton.bones[childIndex].globalTransform.Offset;
                    var delta = tail - head;
                    limbLength = Math.Max(0.0001, (double)delta.Magnitude);
                }
                else
                {
                    // Leaf bone fallback: small but non-zero length.
                    limbLength = 1.0;
                }
            }

            var properties70 = new FbxNode { Name = "Properties70" };

            // Size: affects initial bone radius/display - small value for thin bone display
            var size = new FbxNode { Name = "P" };
            size.Properties.Add("Size");
            size.Properties.Add("double");
            size.Properties.Add("Number");
            size.Properties.Add("");
            size.Properties.Add(0.001);
            properties70.Nodes.Add(size);

            // LimbLength: affects initial tail placement / bone length heuristics.
            var limbLen = new FbxNode { Name = "P" };
            limbLen.Properties.Add("LimbLength");
            limbLen.Properties.Add("double");
            limbLen.Properties.Add("Number");
            limbLen.Properties.Add("");
            limbLen.Properties.Add(limbLength);
            properties70.Nodes.Add(limbLen);

            nodeAttr.Nodes.Add(properties70);

            return nodeAttr;
        }

        private FbxNode CreateSkinDeformerNode(long id)
        {
            var deformer = new FbxNode { Name = "Deformer" };
            deformer.Properties.Add(id);
            deformer.Properties.Add($"Deformer::Skin_{id}");
            deformer.Properties.Add("Skin");

            var version = new FbxNode { Name = "Version", Properties = { 101 } };
            deformer.Nodes.Add(version);

            var link_deformAcuracy = new FbxNode { Name = "Link_DeformAcuracy", Properties = { 50.0 } };
            deformer.Nodes.Add(link_deformAcuracy);

            return deformer;
        }

        private FbxNode CreateClusterNode(FbxMesh mesh, int boneIndex, long clusterId, long boneStartId)
        {
            var cluster = new FbxNode { Name = "Deformer" };
            cluster.Properties.Add(clusterId);
            cluster.Properties.Add($"SubDeformer::Cluster_{clusterId}");
            cluster.Properties.Add("Cluster");

            var version = new FbxNode { Name = "Version", Properties = { 100 } };
            cluster.Nodes.Add(version);

            var mode = new FbxNode { Name = "Mode", Properties = { "Normalize" } };
            cluster.Nodes.Add(mode);

            var userDataNode = new FbxNode { Name = "UserData", Properties = { "", "" } };
            cluster.Nodes.Add(userDataNode);

            // Collect vertex indices and weights for this bone
            var indices = new List<int>();
            var weights = new List<double>();

            foreach (var bw in mesh.boneWeights)
            {
                if (bw.boneIndex == boneIndex)
                {
                    indices.Add(bw.vertexIndex);
                    weights.Add(bw.weight);
                }
            }

            // Indices
            var indexesNode = new FbxNode { Name = "Indexes" };
            indexesNode.Properties.Add(indices.ToArray());
            cluster.Nodes.Add(indexesNode);

            // Weights
            var weightsNode = new FbxNode { Name = "Weights" };
            weightsNode.Properties.Add(weights.ToArray());
            cluster.Nodes.Add(weightsNode);

            // Get the bone's global transform at bind pose
            var bone = skeleton.bones[boneIndex];

            // The mesh model is exported at identity (axis baked into vertex data)
            Matrix4D meshWorldMatrix = Matrix4D.Identity;

            // Bone global transform is already in the correct coordinate system
            // (ROOT was transformed, children inherit that transformation)
            var boneWorldMatrix = bone.globalTransform;

            // FBX Cluster matrices:
            // - Transform: geometry (mesh model) global transform at bind time
            // - TransformLink: bone global transform at bind time
            // Importers derive the inverse bind via TransformLink^-1 * Transform.
            // NOTE: FBX stores matrices as a flat array in column-major order.
            // Our Matrix4D is stored/flattened row-major, so we transpose before writing.
            var transform = new FbxNode { Name = "Transform" };
            transform.Properties.Add(meshWorldMatrix.Transpose().Values);
            cluster.Nodes.Add(transform);

            // TransformLink: Bone's global transform at bind pose
            var transformLink = new FbxNode { Name = "TransformLink" };
            transformLink.Properties.Add(boneWorldMatrix.Transpose().Values);
            cluster.Nodes.Add(transformLink);

            // Optional but used by Blender: associate model (armature object). We use identity.
            var transformAssociateModel = new FbxNode { Name = "TransformAssociateModel" };
            transformAssociateModel.Properties.Add(Matrix4D.Identity.Transpose().Values);
            cluster.Nodes.Add(transformAssociateModel);

            return cluster;
        }

        private FbxNode CreateBindPoseNode(long boneStartId)
        {
            var pose = new FbxNode { Name = "Pose" };
            pose.Properties.Add(5000000L);
            pose.Properties.Add("Pose::BindPose");
            pose.Properties.Add("BindPose");

            var type = new FbxNode { Name = "Type", Properties = { "BindPose" } };
            pose.Nodes.Add(type);

            var version = new FbxNode { Name = "Version", Properties = { 100 } };
            pose.Nodes.Add(version);

            var nbPoseNodes = new FbxNode { Name = "NbPoseNodes", Properties = { skeleton.bones.Count + meshes.Count } };
            pose.Nodes.Add(nbPoseNodes);

            // Add pose node for each bone
            for (int i = 0; i < skeleton.bones.Count; i++)
            {
                var bone = skeleton.bones[i];
                long boneId = boneStartId + i;

                var poseNode = new FbxNode { Name = "PoseNode" };

                var node = new FbxNode { Name = "Node", Properties = { boneId } };
                poseNode.Nodes.Add(node);

                // Bone global transform is already in correct coordinate system
                var boneWorldMatrix = bone.globalTransform;
                var matrix = new FbxNode { Name = "Matrix" };
                matrix.Properties.Add(boneWorldMatrix.Transpose().Values);
                poseNode.Nodes.Add(matrix);

                pose.Nodes.Add(poseNode);
            }

            // Add pose node for each mesh model (identity, since mesh is at root with baked axis)
            long objectId = 1000000;
            for (int m = 0; m < meshes.Count; m++)
            {
                long geometryId = objectId++;
                long modelId = objectId++;
                long materialId = objectId++;

                var poseNode = new FbxNode { Name = "PoseNode" };
                var node = new FbxNode { Name = "Node", Properties = { modelId } };
                poseNode.Nodes.Add(node);

                var matrix = new FbxNode { Name = "Matrix" };
                matrix.Properties.Add(Matrix4D.Identity.Transpose().Values);
                poseNode.Nodes.Add(matrix);

                pose.Nodes.Add(poseNode);
            }

            return pose;
        }

        private FbxNode CreateGeometryNode(FbxMesh mesh, long id)
        {
            var geometry = new FbxNode { Name = "Geometry" };
            geometry.Properties.Add(id);
            geometry.Properties.Add($"Geometry::{mesh.meshName}");
            geometry.Properties.Add("Mesh");

            // Vertices
            var vertices = new FbxNode { Name = "Vertices" };
            var vertexData = new List<double>();
            foreach (var pos in mesh.positions)
            {
                vertexData.Add(pos.X);
                vertexData.Add(pos.Y);
                vertexData.Add(pos.Z);
            }
            vertices.Properties.Add(vertexData.ToArray());
            geometry.Nodes.Add(vertices);

            // Polygon vertex indices
            var polyVertexIndex = new FbxNode { Name = "PolygonVertexIndex" };
            var indexData = new List<int>();
            for (int i = 0; i < mesh.faceIndices.Length; i += 3)
            {
                indexData.Add((int)mesh.faceIndices[i]);
                indexData.Add((int)mesh.faceIndices[i + 1]);
                indexData.Add(-(int)mesh.faceIndices[i + 2] - 1); // Negative indicates end of polygon
            }
            polyVertexIndex.Properties.Add(indexData.ToArray());
            geometry.Nodes.Add(polyVertexIndex);

            // Layer 0
            var layer = new FbxNode { Name = "Layer", Properties = { 0 } };
            layer.Nodes.Add(new FbxNode { Name = "Version", Properties = { 100 } });

            // Layer element normals
            if (mesh.normals != null && mesh.normals.Length > 0)
            {
                var layerElementNormal = new FbxNode { Name = "LayerElementNormal", Properties = { 0 } };
                layerElementNormal.Nodes.Add(new FbxNode { Name = "Version", Properties = { 101 } });
                layerElementNormal.Nodes.Add(new FbxNode { Name = "Name", Properties = { "" } });
                layerElementNormal.Nodes.Add(new FbxNode { Name = "MappingInformationType", Properties = { "ByPolygonVertex" } });
                layerElementNormal.Nodes.Add(new FbxNode { Name = "ReferenceInformationType", Properties = { "IndexToDirect" } });

                // Export unique normals
                var normals = new FbxNode { Name = "Normals" };
                var normalData = new List<double>();
                foreach (var normal in mesh.normals)
                {
                    normalData.Add(normal.X);
                    normalData.Add(normal.Y);
                    normalData.Add(normal.Z);
                }
                normals.Properties.Add(normalData.ToArray());
                layerElementNormal.Nodes.Add(normals);

                // Export normal indices (same as vertex indices for this case)
                var normalIndex = new FbxNode { Name = "NormalsIndex" };
                var normalIndexData = new List<int>();
                for (int i = 0; i < mesh.faceIndices.Length; i++)
                {
                    normalIndexData.Add((int)mesh.faceIndices[i]);
                }
                normalIndex.Properties.Add(normalIndexData.ToArray());
                layerElementNormal.Nodes.Add(normalIndex);

                geometry.Nodes.Add(layerElementNormal);

                var layerNormal = new FbxNode { Name = "LayerElement" };
                layerNormal.Nodes.Add(new FbxNode { Name = "Type", Properties = { "LayerElementNormal" } });
                layerNormal.Nodes.Add(new FbxNode { Name = "TypedIndex", Properties = { 0 } });
                layer.Nodes.Add(layerNormal);
            }

            // Layer element UV
            if (mesh.uvs != null && mesh.uvs.Length > 0)
            {
                var layerElementUV = new FbxNode { Name = "LayerElementUV", Properties = { 0 } };
                layerElementUV.Nodes.Add(new FbxNode { Name = "Version", Properties = { 101 } });
                layerElementUV.Nodes.Add(new FbxNode { Name = "Name", Properties = { "UVMap" } });
                layerElementUV.Nodes.Add(new FbxNode { Name = "MappingInformationType", Properties = { "ByPolygonVertex" } });
                layerElementUV.Nodes.Add(new FbxNode { Name = "ReferenceInformationType", Properties = { "IndexToDirect" } });

                // Export unique UVs
                var uvs = new FbxNode { Name = "UV" };
                var uvData = new List<double>();
                foreach (var uv in mesh.uvs)
                {
                    uvData.Add(uv.X);
                    uvData.Add(uv.Y);
                }
                uvs.Properties.Add(uvData.ToArray());
                layerElementUV.Nodes.Add(uvs);

                // Export UV indices (same as vertex indices for this case)
                var uvIndex = new FbxNode { Name = "UVIndex" };
                var uvIndexData = new List<int>();
                for (int i = 0; i < mesh.faceIndices.Length; i++)
                {
                    uvIndexData.Add((int)mesh.faceIndices[i]);
                }
                uvIndex.Properties.Add(uvIndexData.ToArray());
                layerElementUV.Nodes.Add(uvIndex);

                geometry.Nodes.Add(layerElementUV);

                var layerUV = new FbxNode { Name = "LayerElement" };
                layerUV.Nodes.Add(new FbxNode { Name = "Type", Properties = { "LayerElementUV" } });
                layerUV.Nodes.Add(new FbxNode { Name = "TypedIndex", Properties = { 0 } });
                layer.Nodes.Add(layerUV);
            }

            // Layer element material
            var layerElementMaterial = new FbxNode { Name = "LayerElementMaterial", Properties = { 0 } };
            layerElementMaterial.Nodes.Add(new FbxNode { Name = "Version", Properties = { 101 } });
            layerElementMaterial.Nodes.Add(new FbxNode { Name = "Name", Properties = { "" } });
            layerElementMaterial.Nodes.Add(new FbxNode { Name = "MappingInformationType", Properties = { "AllSame" } });
            layerElementMaterial.Nodes.Add(new FbxNode { Name = "ReferenceInformationType", Properties = { "IndexToDirect" } });
            layerElementMaterial.Nodes.Add(new FbxNode { Name = "Materials", Properties = { new int[] { 0 } } });
            geometry.Nodes.Add(layerElementMaterial);

            var layerMaterial = new FbxNode { Name = "LayerElement" };
            layerMaterial.Nodes.Add(new FbxNode { Name = "Type", Properties = { "LayerElementMaterial" } });
            layerMaterial.Nodes.Add(new FbxNode { Name = "TypedIndex", Properties = { 0 } });
            layer.Nodes.Add(layerMaterial);

            // Add smoothing groups to prevent all edges being marked as sharp
            var layerElementSmoothing = new FbxNode { Name = "LayerElementSmoothing", Properties = { 0 } };
            layerElementSmoothing.Nodes.Add(new FbxNode { Name = "Version", Properties = { 102 } });
            layerElementSmoothing.Nodes.Add(new FbxNode { Name = "Name", Properties = { "" } });
            layerElementSmoothing.Nodes.Add(new FbxNode { Name = "MappingInformationType", Properties = { "ByPolygon" } });
            layerElementSmoothing.Nodes.Add(new FbxNode { Name = "ReferenceInformationType", Properties = { "Direct" } });

            // Set all faces to smoothing group 1 (smooth shading)
            var smoothing = new FbxNode { Name = "Smoothing" };
            var smoothingData = new int[mesh.faceIndices.Length / 3];
            for (int i = 0; i < smoothingData.Length; i++)
            {
                smoothingData[i] = 1;
            }
            smoothing.Properties.Add(smoothingData);
            layerElementSmoothing.Nodes.Add(smoothing);
            geometry.Nodes.Add(layerElementSmoothing);

            var layerSmoothing = new FbxNode { Name = "LayerElement" };
            layerSmoothing.Nodes.Add(new FbxNode { Name = "Type", Properties = { "LayerElementSmoothing" } });
            layerSmoothing.Nodes.Add(new FbxNode { Name = "TypedIndex", Properties = { 0 } });
            layer.Nodes.Add(layerSmoothing);

            geometry.Nodes.Add(layer);

            return geometry;
        }

        private FbxNode CreateModelNode(FbxMesh mesh, long id)
        {
            var model = new FbxNode { Name = "Model" };
            model.Properties.Add(id);
            model.Properties.Add($"Model::{mesh.meshName}");
            model.Properties.Add("Mesh");

            var version = new FbxNode { Name = "Version", Properties = { 232 } };
            model.Nodes.Add(version);

            var properties70 = new FbxNode { Name = "Properties70" };

            // Default visibility
            var defaultVisibility = new FbxNode { Name = "P" };
            defaultVisibility.Properties.Add("DefaultAttributeIndex");
            defaultVisibility.Properties.Add("int");
            defaultVisibility.Properties.Add("Integer");
            defaultVisibility.Properties.Add("");
            defaultVisibility.Properties.Add(0);
            properties70.Nodes.Add(defaultVisibility);

            model.Nodes.Add(properties70);

            return model;
        }

        private FbxNode CreateMaterialNode(FbxMesh mesh, long id)
        {
            var material = new FbxNode { Name = "Material" };
            material.Properties.Add(id);
            material.Properties.Add($"Material::{mesh.meshName}_Mat");
            material.Properties.Add("");

            var version = new FbxNode { Name = "Version", Properties = { 102 } };
            material.Nodes.Add(version);

            var shadingModel = new FbxNode { Name = "ShadingModel", Properties = { "phong" } };
            material.Nodes.Add(shadingModel);

            var multiLayer = new FbxNode { Name = "MultiLayer", Properties = { 0 } };
            material.Nodes.Add(multiLayer);

            var properties70 = new FbxNode { Name = "Properties70" };

            // Diffuse color
            var diffuseColor = new FbxNode { Name = "P" };
            diffuseColor.Properties.Add("DiffuseColor");
            diffuseColor.Properties.Add("Color");
            diffuseColor.Properties.Add("");
            diffuseColor.Properties.Add("A");
            diffuseColor.Properties.Add(0.8);
            diffuseColor.Properties.Add(0.8);
            diffuseColor.Properties.Add(0.8);
            properties70.Nodes.Add(diffuseColor);

            // Ambient color
            var ambientColor = new FbxNode { Name = "P" };
            ambientColor.Properties.Add("AmbientColor");
            ambientColor.Properties.Add("Color");
            ambientColor.Properties.Add("");
            ambientColor.Properties.Add("A");
            ambientColor.Properties.Add(0.2);
            ambientColor.Properties.Add(0.2);
            ambientColor.Properties.Add(0.2);
            properties70.Nodes.Add(ambientColor);

            // Blender material tuning (imported via FBX -> Principled BSDF mapping):
            // - Roughness ~= 1  => Shininess = 0
            // - Specular ~= 0    => SpecularFactor = 0 (and SpecularColor = black)
            var specularColor = new FbxNode { Name = "P" };
            specularColor.Properties.Add("SpecularColor");
            specularColor.Properties.Add("Color");
            specularColor.Properties.Add("");
            specularColor.Properties.Add("A");
            specularColor.Properties.Add(0.0);
            specularColor.Properties.Add(0.0);
            specularColor.Properties.Add(0.0);
            properties70.Nodes.Add(specularColor);

            var specularFactor = new FbxNode { Name = "P" };
            specularFactor.Properties.Add("SpecularFactor");
            specularFactor.Properties.Add("Number");
            specularFactor.Properties.Add("");
            specularFactor.Properties.Add("A");
            specularFactor.Properties.Add(0.0);
            properties70.Nodes.Add(specularFactor);

            var shininess = new FbxNode { Name = "P" };
            shininess.Properties.Add("Shininess");
            shininess.Properties.Add("Number");
            shininess.Properties.Add("");
            shininess.Properties.Add("A");
            shininess.Properties.Add(0.0);
            properties70.Nodes.Add(shininess);

            var indexOfRefraction = new FbxNode { Name = "P" };
            indexOfRefraction.Properties.Add("IndexOfRefraction");
            indexOfRefraction.Properties.Add("Number");
            indexOfRefraction.Properties.Add("");
            indexOfRefraction.Properties.Add("A");
            indexOfRefraction.Properties.Add(1.0);
            properties70.Nodes.Add(indexOfRefraction);

            material.Nodes.Add(properties70);

            return material;
        }

        private FbxNode CreateConnections(bool linkTextures, bool glassTextures, bool wingTextures)
        {
            var connections = new FbxNode { Name = "Connections" };

            long objectId = 1000000;
            long boneStartId = 2000000;
            long skinStartId = 3000000;
            long clusterStartId = 4000000;

            // Connect skeleton bones
            if (skeleton != null && skeleton.bones.Count > 0)
            {
                for (int i = 0; i < skeleton.bones.Count; i++)
                {
                    var bone = skeleton.bones[i];
                    long boneId = boneStartId + i;
                    long boneAttrId = boneStartId + skeleton.bones.Count + i;

                    // Connect bone attribute to bone model
                    var attrToBone = new FbxNode { Name = "C", Properties = { "OO", boneAttrId, boneId } };
                    connections.Nodes.Add(attrToBone);

                    // Connect bone to parent or root
                    if (bone.parentIndex >= 0)
                    {
                        long parentBoneId = boneStartId + bone.parentIndex;
                        var boneToParent = new FbxNode { Name = "C", Properties = { "OO", boneId, parentBoneId } };
                        connections.Nodes.Add(boneToParent);
                    }
                    else
                    {
                        // Root bone connects to scene root
                        var boneToRoot = new FbxNode { Name = "C", Properties = { "OO", boneId, 0L } };
                        connections.Nodes.Add(boneToRoot);
                    }
                }
            }

            // Connect meshes, skins, and clusters
            long currentClusterId = clusterStartId;
            for (int m = 0; m < meshes.Count; m++)
            {
                var mesh = meshes[m];
                long geometryId = objectId++;
                long modelId = objectId++;
                long materialId = objectId++;
                long skinId = skinStartId + m;

                // Connect Geometry to Model
                var geoToModel = new FbxNode { Name = "C", Properties = { "OO", geometryId, modelId } };
                connections.Nodes.Add(geoToModel);

                // Connect Material to Model
                var matToModel = new FbxNode { Name = "C", Properties = { "OO", materialId, modelId } };
                connections.Nodes.Add(matToModel);

                // Connect textures to material (property connections) so Blender loads images automatically.
                if (linkTextures && exportTextures != null && exportTextures.Count > 0)
                {
                    bool isGlassMesh = mesh.shader == (uint)SimShader.SimGlass ||
                                       (mesh.meshName != null && mesh.meshName.EndsWith("_glass", StringComparison.OrdinalIgnoreCase));
                    bool isWingsMesh = mesh.shader == (uint)SimShader.SimWings ||
                                       (mesh.meshName != null && mesh.meshName.EndsWith("_wings", StringComparison.OrdinalIgnoreCase));

                    foreach (var tex in exportTextures)
                    {
                        // Always wire normal/emission to all materials (if present).
                        // Diffuse/specular pick base vs glass vs wings depending on mesh name.
                        if (tex.Target == TextureTarget.Base)
                        {
                            if (tex.Kind == TextureKind.Diffuse || tex.Kind == TextureKind.Specular)
                            {
                                if (isGlassMesh || isWingsMesh)
                                    continue;
                            }
                        }
                        else if (tex.Target == TextureTarget.Glass)
                        {
                            if (!isGlassMesh)
                                continue;
                        }
                        else if (tex.Target == TextureTarget.Wings)
                        {
                            if (!isWingsMesh)
                                continue;
                        }

                        // OO: Video -> Texture
                        connections.Nodes.Add(new FbxNode { Name = "C", Properties = { "OO", tex.VideoId, tex.TextureId } });

                        // OP: Texture -> Material.<Property>
                        connections.Nodes.Add(new FbxNode { Name = "C", Properties = { "OP", tex.TextureId, materialId, tex.MaterialProperty } });
                    }
                }

                // Connect Model to scene root (NOT to skeleton!)
                // Skinned meshes should be at root level, not parented to bones
                // The skinning relationship is handled through the skin deformer
                var modelToRoot = new FbxNode { Name = "C", Properties = { "OO", modelId, 0L } };
                connections.Nodes.Add(modelToRoot);

                // Connect skin deformer and clusters if mesh has bone weights
                if (skeleton != null && mesh.boneWeights != null && mesh.boneWeights.Count > 0)
                {
                    // Connect skin to geometry
                    var skinToGeo = new FbxNode { Name = "C", Properties = { "OO", skinId, geometryId } };
                    connections.Nodes.Add(skinToGeo);

                    // Connect clusters to skin and bones
                    var bonesInMesh = new HashSet<int>(mesh.boneWeights.Select(w => w.boneIndex));
                    foreach (int boneIndex in bonesInMesh)
                    {
                        long clusterId = currentClusterId++;
                        long boneId = boneStartId + boneIndex;

                        // Connect cluster to skin
                        var clusterToSkin = new FbxNode { Name = "C", Properties = { "OO", clusterId, skinId } };
                        connections.Nodes.Add(clusterToSkin);

                        // Connect bone to cluster
                        var boneToCluster = new FbxNode { Name = "C", Properties = { "OO", boneId, clusterId } };
                        connections.Nodes.Add(boneToCluster);
                    }
                }
            }

            return connections;
        }

        private enum TextureTarget
        {
            Base,
            Glass,
            Wings
        }

        private enum TextureKind
        {
            Diffuse,
            Specular,
            Normal,
            Emission
        }

        private sealed class ExportTexture
        {
            public TextureTarget Target;
            public TextureKind Kind;
            public string FileName;
            public string TextureName;
            public string VideoName;
            public string MaterialProperty;
            public long TextureId;
            public long VideoId;
        }

        private static List<ExportTexture> PrepareExportTextures(string directory, string basename, bool linkTextures, bool glassTextures, bool wingTextures)
        {
            var results = new List<ExportTexture>();
            if (!linkTextures)
                return results;

            // IDs live in their own range to avoid collision with geometry/material/model IDs.
            long videoId = 5000000;
            long textureId = 6000000;

            void Add(TextureTarget target, TextureKind kind, string suffix, string materialProperty, bool requireFile)
            {
                string filename = basename + suffix;
                if (requireFile && !string.IsNullOrWhiteSpace(directory))
                {
                    string fullPath = Path.Combine(directory, filename);
                    if (!File.Exists(fullPath))
                        return;
                }

                string name = basename + suffix.Replace(".", "_").Replace("-", "_");
                results.Add(new ExportTexture
                {
                    Target = target,
                    Kind = kind,
                    FileName = filename,
                    TextureName = name,
                    VideoName = name,
                    MaterialProperty = materialProperty,
                    VideoId = videoId++,
                    TextureId = textureId++
                });
            }

            // Base textures (match PreviewControl.SaveModelMorph naming).
            Add(TextureTarget.Base, TextureKind.Diffuse, "_diffuse.png", "DiffuseColor", requireFile: false);
            Add(TextureTarget.Base, TextureKind.Specular, "_specular.png", "SpecularColor", requireFile: false);
            Add(TextureTarget.Base, TextureKind.Normal, "_normalmap.png", "NormalMap", requireFile: false);
            Add(TextureTarget.Base, TextureKind.Emission, "_emissionmap.png", "EmissiveColor", requireFile: true);

            // Optional separated shader textures.
            if (glassTextures)
            {
                Add(TextureTarget.Glass, TextureKind.Diffuse, "_glass_diffuse.png", "DiffuseColor", requireFile: false);
                Add(TextureTarget.Glass, TextureKind.Specular, "_glass_specular.png", "SpecularColor", requireFile: false);
            }

            if (wingTextures)
            {
                Add(TextureTarget.Wings, TextureKind.Diffuse, "_wings_diffuse.png", "DiffuseColor", requireFile: false);
                Add(TextureTarget.Wings, TextureKind.Specular, "_wings_specular.png", "SpecularColor", requireFile: false);
            }

            return results;
        }

        // Helper classes
        public class FbxMesh
        {
            public string meshID;
            public string meshName;
            public Vector3[] positions;
            public Vector3[] normals;
            public Vector2[] uvs;
            public uint[] faceIndices;
            public uint shader;
            public List<BoneWeight> boneWeights;
        }

        public class BoneWeight
        {
            public int vertexIndex;
            public int boneIndex;
            public float weight;
        }

        public class FbxSkeleton
        {
            public List<FbxBone> bones = new List<FbxBone>();
        }

        public class FbxBone
        {
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public Matrix4D localTransform;
            public Matrix4D globalTransform;
            public int parentIndex = -1;
        }
    }
}
