// Starcraft: Manifold - keeps other effects off the console and the menus.
//
// Put ManifoldMask_Top above the effects that should only show in the game
// view and ManifoldMask_Bottom below them. Top saves the frame; Bottom puts
// the saved frame back everywhere except where effects are allowed. The
// Manifold plugin uploads that area (EffectsTex) when a game starts: the game
// view, around the console of the race being played at the current
// resolution. It empties it when the game ends, so the menus have no effects.
//
// The texture starts empty, which means "no effects anywhere". So without the
// plugin, or with a ReShade that refuses it, the effects between the two
// techniques do nothing at all.
//
// Effects placed after ManifoldMask_Bottom are not masked.

namespace ManifoldUIMask
{

uniform bool DisplayMask <
	ui_label = "Display Mask";
	ui_tooltip = "Tints the area that is kept free of effects magenta. For checking only.\n"
	             "Outside a game that is the whole screen.";
> = false;

texture BackBufferTex : COLOR;
sampler BackBuffer { Texture = BackBufferTex; };

texture BackupTex { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; };
sampler Backup { Texture = BackupTex; };

// Where effects are allowed. Filled by the plugin through ReShade's add-on API;
// ReShade clears it to zero, which allows them nowhere.
texture EffectsTex { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = R8; };
sampler Effects { Texture = EffectsTex; };

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
	float effects = tex2D(Effects, texcoord).r;
	float4 color = lerp(tex2D(Backup, texcoord), tex2D(BackBuffer, texcoord), effects);
	if (DisplayMask)
		color.rgb = lerp(color.rgb, float3(1.0, 0.0, 1.0), (1.0 - effects) * 0.5);
	return color;
}

technique ManifoldMask_Top <
	ui_tooltip = "Place this above the effects to keep off the console and the menus.";
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
	ui_tooltip = "Place this below the effects to keep off the console and the menus.";
>
{
	pass
	{
		VertexShader = FullscreenVS;
		PixelShader = RestorePS;
	}
}

} // namespace ManifoldUIMask
