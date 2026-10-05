using SonicWorldTools.Core;
using Xunit;

namespace SonicWorldTools.Tests
{
	public sealed class SelectionVolumeTests
	{
		[Fact]
		public void Normalize_orders_corners_independently_per_axis()
		{
			SelectionVolumeMath.Normalize(
				10f,
				-2f,
				5f,
				1f,
				3f,
				-1f,
				out float minX,
				out float minY,
				out float minZ,
				out float maxX,
				out float maxY,
				out float maxZ);

			Assert.Equal(1f, minX);
			Assert.Equal(-2f, minY);
			Assert.Equal(-1f, minZ);
			Assert.Equal(10f, maxX);
			Assert.Equal(3f, maxY);
			Assert.Equal(5f, maxZ);
		}

		[Fact]
		public void Normalize_is_idempotent_when_already_ordered()
		{
			SelectionVolumeMath.Normalize(
				0f,
				0f,
				0f,
				4f,
				8f,
				2f,
				out float minX,
				out float minY,
				out float minZ,
				out float maxX,
				out float maxY,
				out float maxZ);

			Assert.Equal(0f, minX);
			Assert.Equal(0f, minY);
			Assert.Equal(0f, minZ);
			Assert.Equal(4f, maxX);
			Assert.Equal(8f, maxY);
			Assert.Equal(2f, maxZ);
		}
	}
}
