using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;

public class TexturePacker : MonoBehaviour
{
	[SerializeField]
	private Texture[] m_Textures = { };
	[SerializeField]
	private int m_Border = 0;
	[SerializeField]
	private int m_MaxSize = 4096;

	private RenderTexture m_Atlas;
	private Dictionary<Texture, Rect> m_Rects = new Dictionary<Texture, Rect>();

	public void Pack()
	{
		Render(CreateTree());
	}

	public Texture GetAtlas()
	{
		return m_Atlas;
	}

	public Rect GetRect(Texture a_Texture)
	{
		return m_Rects[a_Texture];
	}

	private TreeNode CreateTree()
	{
		var textures = m_Textures.OrderByDescending(texture => texture.width * texture.height);

		TreeNode root = new TreeNode { Width = 2, Height = 2, Border = m_Border };

		while (true)
		{
			if (root.Pack(textures))
			{
				return root;
			}

			if (root.Width <= root.Height)
			{
				root = new TreeNode { Width = root.Width * 2, Height = root.Height, Border = m_Border };
			}
			else
			{
				root = new TreeNode { Width = root.Width, Height = root.Height * 2, Border = m_Border };
			}

			if (root.Width > m_MaxSize || root.Height > m_MaxSize)
			{
				throw new System.InvalidOperationException("Max size exceeded.");
			}
		}
	}

	private void Render(TreeNode a_Tree)
	{
		var textureNodes = a_Tree.TextureNodes.ToDictionary(node => node.Texture);
		m_Rects = m_Textures.ToDictionary(texture => texture, texture => {
			TreeNode node = textureNodes[texture];
			return new Rect(
				(float)node.X / a_Tree.Width,
				(float)node.Y / a_Tree.Height,
				(float)texture.width / a_Tree.Width,
				(float)texture.height / a_Tree.Height
			);
		});

		m_Atlas = new RenderTexture(a_Tree.Width, a_Tree.Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
		m_Atlas.depth = 0;
		m_Atlas.useMipMap = true;
		m_Atlas.isPowerOfTwo = true;
		m_Atlas.name = "Atlas";

		Mesh mesh = new Mesh();
		mesh.vertices = new Vector3[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0) };
		mesh.uv = new Vector2[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
		mesh.triangles = new int[] { 0, 1, 2, 1, 3, 2 };
		Material material = new Material(Shader.Find("TexturePacker/Blit"));

		using (CommandBuffer commandBuffer = new CommandBuffer()) {
			commandBuffer.SetRenderTarget(m_Atlas);
			commandBuffer.ClearRenderTarget(true, true, Color.black);

			Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(-1, -1, 0), Quaternion.identity, new Vector3(2, 2, 2));

			float borderWidth = (float)m_Border / a_Tree.Width;
			float borderHeight = (float)m_Border / a_Tree.Height;
			var propertyBlocks = m_Textures.Select(texture => {
				Rect rect = m_Rects[texture];
				Matrix4x4 drawMatrix = Matrix4x4.TRS(new Vector3(rect.x - borderWidth / 2, rect.y - borderHeight / 2, 0), Quaternion.identity, new Vector3(rect.width + borderWidth, rect.height + borderHeight, 0));
				MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
				propertyBlock.SetTexture("_MainTex", texture);
				propertyBlock.SetVector("_MainTex_ST", new Vector4(1 + (float)m_Border / texture.width, 1 + (float)m_Border / texture.height, (float)m_Border / texture.width / -2, (float)m_Border / texture.height / -2));
				propertyBlock.SetMatrix("MVP", matrix * drawMatrix);
				return propertyBlock;
			});

			foreach (var propertyBlock in propertyBlocks)
			{
				commandBuffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, propertyBlock);
			}

			Graphics.SetRenderTarget(m_Atlas);
			Graphics.ExecuteCommandBuffer(commandBuffer);
		}

		Destroy(mesh);
		Destroy(material);
	}

	private class TreeNode
	{
		public int X;
		public int Y;
		public int Width;
		public int Height;
		public int Border;
		public TreeNode Leaf1;
		public TreeNode Leaf2;
		public Texture Texture;

		public IEnumerable<TreeNode> TextureNodes
		{
			get
			{
				if (Texture != null)
				{
					yield return this;
				}
				if (Leaf1 != null)
				{
					foreach (TreeNode node in Leaf1.TextureNodes)
					{
						yield return node;
					}
				}
				if (Leaf2 != null)
				{
					foreach (TreeNode node in Leaf2.TextureNodes)
					{
						yield return node;
					}
				}
			}
		}

		public bool Pack(IEnumerable<Texture> a_Textures)
		{
			foreach (Texture texture in a_Textures)
			{
				if (!Pack(texture))
				{
					return false;
				}
			}
			return true;
		}

		public bool Pack(Texture a_Texture)
		{
			if (Leaf1 == null && Leaf2 == null)
			{
				if (a_Texture.width <= Width && a_Texture.height <= Height)
				{
					Texture = a_Texture;

					if (Width - Texture.width < Height - Texture.height)
					{
						//	split so the top is cut in half and the rest is one big rect below
						Leaf1 = new TreeNode { X = X + Texture.width + Border, Y = Y, Width = Width - Texture.width - Border, Height = Texture.height, Border = Border };
						Leaf2 = new TreeNode { X = X, Y = Y + Texture.height + Border, Width = Width, Height = Height - Texture.height - Border, Border = Border };
					}
					else
					{
						//	split so the left is cut in half and the rest is one big rect to the right
						Leaf1 = new TreeNode { X = X, Y = Y + Texture.height + Border, Width = Texture.width, Height = Height - Texture.height - Border, Border = Border };
						Leaf2 = new TreeNode { X = X + Texture.width + Border, Y = Y, Width = Width - Texture.width - Border, Height = Height, Border = Border };
					}
					return true;
				}
				return false;
			}

			return Leaf1.Pack(a_Texture) || Leaf2.Pack(a_Texture);
		}
	}
}
