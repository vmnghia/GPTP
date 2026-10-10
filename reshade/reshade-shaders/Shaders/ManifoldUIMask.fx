// Starcraft: Manifold - keeps other effects off the console.
//
// Put ManifoldMask_Top above the effects that should stay off the console and
// ManifoldMask_Bottom below them. Top saves the frame; Bottom puts the saved
// frame back wherever the mask says "UI". The Manifold plugin uploads the mask
// (MaskTex) when a game starts, for the console of the race being played at
// the current resolution, and empties it when the game ends. Without the
// plugin the mask stays empty and the pair changes nothing.
//
// Effects placed after ManifoldMask_Bottom are not masked.

namespace ManifoldUIMask
{

uniform bool DisplayMask <
	ui_label = "Display Mask";
	ui_tooltip = "Shows the mask instead of the picture: white is kept free of effects.\n"
	             "For checking only. Outside a game the mask is empty, so the screen is black.";
> = false;

texture BackBufferTex : COLOR;
sampler BackBuffer { Texture = BackBufferTex; };

texture BackupTex { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; };
sampler Backup { Texture = BackupTex; };

// Filled by the plugin through ReShade's add-on API; ReShade clears it to zero.
texture MaskTex { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = R8; };
sampler Mask { Texture = MaskTex; };

void FullscreenVS(in uint id : SV_VertexID, out float4 position : SV_Position, out float2 texcoord : TEXCOORD)
{
	texcoord.x = (id == 2) ? 2.0 : 0.0;
	texcoord.y = (id == 1) ? 2.0 : 0.0;
	position = float4(texcoord * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}

float4 BackupPS(float4 position : SV_Position, float2 texcoord : TEXCOORD) : SV_Target
{
	return tex2D(BackBuffer, texcoord);
}

float4 RestorePS(float4 position : SV_Position, float2 texcoord : TEXCOORD) : SV_Target
{
	float mask = tex2D(Mask, texcoord).r;
	if (DisplayMask)
		return float4(mask, mask, mask, 1.0);
	return lerp(tex2D(BackBuffer, texcoord), tex2D(Backup, texcoord), mask);
}

technique ManifoldMask_Top <
	ui_tooltip = "Place this above the effects to keep off the console.";
>
{
	pass
	{
		VertexShader = FullscreenVS;
		PixelShader = BackupPS;
		RenderTarget = BackupTex;
	}
}

technique ManifoldMask_Bottom <
	ui_tooltip = "Place this below the effects to keep off the console.";
>
{
	pass
	{
		VertexShader = FullscreenVS;
		PixelShader = RestorePS;
	}
}

} // namespace ManifoldUIMask
