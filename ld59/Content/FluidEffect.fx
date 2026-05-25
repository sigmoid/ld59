#ifndef RENDERTARGETSIZE_DECLARED
float2 renderTargetSize;
#define RENDERTARGETSIZE_DECLARED
#endif
#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

VertexShaderOutput MainVS(VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;
    float2 ndc = (input.Position.xy / renderTargetSize) * 2.0 - 1.0;
    
    output.Position = float4(ndc, 0, 1);
    output.TexCoord = float2(input.TexCoord.x, 1.0 - input.TexCoord.y);
    return output;
}

float timeStep;
float diffusion;
float2 texelSize;
float2 cursorPosition;
float2 cursorValue;
float radius;
float ignitionTemperature;
float fuelBurnTemperature;
float fuelConsumptionRate;
float vorticityScale;
float combustionPressure;
float ambientTemperature;
float maxTemperature;
float coolingRate;
float minFuelThreshold;
float gravity;
float heatBuoyancyConstant;
float smokeEmissionRate;
float sourceStrength;
// Boundary condition parameters
float boundaryScale;
float2 boundaryOffset;

// Gaussian blur parameters
float blurRadius;
int blurKernelSize;

// Sprite drawing parameters
float2 spritePosition;
float2 spriteScale;
float spriteRotation;
float spriteOpacity;

float velocityDampingCoefficient;
float smokeOpacity;
float2 uvOffset;

texture fuelTexture;
sampler2D fuelSampler = sampler_state
{
    Texture = <fuelTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = NONE;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

texture velocityTexture;
sampler2D velocitySampler = sampler_state
{
    Texture = <velocityTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};


texture sourceTexture;
sampler2D sourceSampler = sampler_state
{
    Texture = <sourceTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

texture pressureTexture;
sampler2D pressureSampler = sampler_state
{
    Texture = <pressureTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

texture divergenceTexture;
sampler2D divergenceSampler = sampler_state
{
    Texture = <divergenceTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

texture temperatureTexture;
sampler2D temperatureSampler = sampler_state
{
    Texture = <temperatureTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

texture vorticityTexture;
sampler2D vorticitySampler = sampler_state
{
    Texture = <vorticityTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

texture obstacleTexture;
sampler2D obstacleSampler = sampler_state
{
    Texture = <obstacleTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

texture flameGradientTexture;
sampler2D flameGradientSampler = sampler_state
{
    Texture = <flameGradientTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

texture smokeTexture;
sampler2D smokeSampler = sampler_state
{
    Texture = <smokeTexture>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Wrap;
};

texture spriteTexture;
sampler2D spriteSampler = sampler_state
{
    Texture = <spriteTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

texture spriteObstacleTexture;
sampler2D spriteObstacleSampler = sampler_state
{
    Texture = <spriteObstacleTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};


float4 DiffusePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = frac(input.TexCoord + uvOffset);
    float4 center = tex2D(sourceSampler, pos);

    if (diffusion <= 0.000001f)
    {
        return center;
    }

    float4 left = tex2D(sourceSampler, frac(pos - float2(texelSize.x, 0)));
    float4 right = tex2D(sourceSampler, frac(pos + float2(texelSize.x, 0)));
    float4 top = tex2D(sourceSampler, frac(pos - float2(0, texelSize.y)));
    float4 bottom = tex2D(sourceSampler, frac(pos + float2(0, texelSize.y)));

    float alpha = ((texelSize.x )*(texelSize.x )) / (diffusion * timeStep);
    float beta = 1.0f / (4.0f + alpha);

    float4 result = (left + right + top + bottom + alpha * center) * beta;

    return result;
}

float4 ComputeDivergencePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    float2 scrollPos = frac(input.TexCoord + uvOffset);

    float obsC = tex2D(obstacleSampler, scrollPos).r;
    if (obsC > 0.1f) {
        return float4(0, 0, 0, 1);
    }

    float obsL = tex2D(obstacleSampler, pos - float2(texelSize.x, 0)).r;
    float obsR = tex2D(obstacleSampler, pos + float2(texelSize.x, 0)).r;
    float obsT = tex2D(obstacleSampler, pos - float2(0, texelSize.y)).r;
    float obsB = tex2D(obstacleSampler, pos + float2(0, texelSize.y)).r;

    float2 vL = tex2D(velocitySampler, frac(scrollPos - float2(texelSize.x, 0))).xy;
    float2 vR = tex2D(velocitySampler, frac(scrollPos + float2(texelSize.x, 0))).xy;
    float2 vT = tex2D(velocitySampler, frac(scrollPos - float2(0, texelSize.y))).xy;
    float2 vB = tex2D(velocitySampler, frac(scrollPos + float2(0, texelSize.y))).xy;

    float halfRdx = 0.5f / texelSize.x;

    float divergence = halfRdx * ((vR.x - vL.x) + (vB.y - vT.y));

    return float4(divergence, 0, 0, 1);
}

float4 VisualizePS(VertexShaderOutput input) : COLOR0
{
    float2 visTexCoord = input.TexCoord;
    float2 scrollTexCoord = frac(input.TexCoord + uvOffset);

    float obstacle = tex2D(obstacleSampler, scrollTexCoord).r;
    float smoke = tex2D(smokeSampler, scrollTexCoord).r;

    float4 finalColor = float4(smoke, smoke, smoke, 1.0);



    return 1.0 - finalColor;
}

float4 VisualizeVelocityPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = frac(input.TexCoord + uvOffset);
    float2 vel = tex2D(velocitySampler, pos).xy;

    float scale = 16.0;
    float2 remapped = saturate(vel * scale * 0.5 + 0.5);
    float mag = saturate(length(vel) * scale);

    return float4(remapped.x, remapped.y, mag, 1.0);
}

float4 ApplyGravityPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float2 velocity = tex2D(velocitySampler, pos).xy;
    
    velocity.y -= gravity * timeStep;

    return float4(velocity, 0, 1);
}

float4 AdvectPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = frac(input.TexCoord + uvOffset);

    float2 velocity = tex2D(velocitySampler, pos).xy;

    float2 prevPos = frac(pos - velocity * texelSize * timeStep);

    float4 result = tex2D(sourceSampler, prevPos);

    return result;
}

float4 ProjectPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float obstacle = tex2D(obstacleSampler, pos).r;

    if(obstacle > 0.1f)
    {
        return float4(0,0,0,1);
    }

    float2 velocity = tex2D(velocitySampler, pos).xy;
    
    float pL = tex2D(pressureSampler, pos - float2(texelSize.x, 0)).x;
    float pR = tex2D(pressureSampler, pos + float2(texelSize.x, 0)).x;
    float pT = tex2D(pressureSampler, pos - float2(0, texelSize.y)).x;
    float pB = tex2D(pressureSampler, pos + float2(0, texelSize.y)).x;
    
    float halfRdx = 0.5f / (texelSize.x );

    float2 gradient = float2(pR - pL, pB - pT) * halfRdx;
    
    float2 res = velocity - gradient;

    float obsL = tex2D(obstacleSampler, pos - float2(texelSize.x, 0)).r;
    float obsR = tex2D(obstacleSampler, pos + float2(texelSize.x, 0)).r;
    float obsT = tex2D(obstacleSampler, pos - float2(0, texelSize.y)).r;
    float obsB = tex2D(obstacleSampler, pos + float2(0, texelSize.y)).r;

    if (obsL > 0.1f && res.x < 0) res.x = 0;
    if (obsR > 0.1f && res.x > 0) res.x = 0;
    if (obsT > 0.1f && res.y < 0) res.y = 0;
    if (obsB > 0.1f && res.y > 0) res.y = 0;
    
    return float4(res, 0, 1);
}

float4 JacobiPressurePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float4 center = tex2D(sourceSampler, pos);

    float obsCenter = tex2D(obstacleSampler, pos).r;
    if(obsCenter > 0.1f) {
        return float4(0, 0, 0, 1);
    }

    float obsL = tex2D(obstacleSampler, pos - float2(texelSize.x, 0)).r;
    float obsR = tex2D(obstacleSampler, pos + float2(texelSize.x, 0)).r;
    float obsT = tex2D(obstacleSampler, pos - float2(0, texelSize.y)).r;
    float obsB = tex2D(obstacleSampler, pos + float2(0, texelSize.y)).r;

    float left   = obsL > 0.1 ? 0.0 : tex2D(sourceSampler, pos - float2(texelSize.x, 0)).r;
    float right  = obsR > 0.1 ? 0.0 : tex2D(sourceSampler, pos + float2(texelSize.x, 0)).r;
    float top    = obsT > 0.1 ? 0.0 : tex2D(sourceSampler, pos - float2(0, texelSize.y)).r;
    float bottom = obsB > 0.1 ? 0.0 : tex2D(sourceSampler, pos + float2(0, texelSize.y)).r;

    float div = tex2D(divergenceSampler, pos).x;

    float alpha = -((texelSize.x )*(texelSize.x ));
    
    float result = (left + right + top + bottom + alpha * div) / 4.0f;
    
    return float4(result,0,0,1);
}

float4 ClampPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    float4 value = tex2D(sourceSampler, pos);
    
    value = saturate(value);
    
    return value;
}

float4 AddValuePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float4 existingValue = tex2D(sourceSampler, pos);

    float dist = distance(pos, cursorPosition);

    if (dist > radius)
    {
        return existingValue;
    }

    float falloff = 1.0 - (dist / radius);

    float2 addedValue = cursorValue * falloff;

    float4 result = existingValue + float4(addedValue, 0, 0);

    return result;
}

float4 AddValueDiffusePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float4 existingValue = tex2D(sourceSampler, pos);

    float dist = distance(pos, cursorPosition);

    float falloff = exp(-(dist * dist) / (2.0 * radius * radius));

    float2 addedValue = cursorValue * falloff;

    return existingValue + float4(addedValue, 0, 0);
}

float4 SetValuePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float dist = distance(pos, cursorPosition);

    if (dist <= radius)
    {
        return float4(cursorValue, 0, 0);
    }
    else
    {
        return tex2D(sourceSampler, pos);
    }
}

float4 ComputeVorticityPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float2 vL = tex2D(velocitySampler, pos - float2(texelSize.x, 0)).xy;
    float2 vR = tex2D(velocitySampler, pos + float2(texelSize.x, 0)).xy;
    float2 vT = tex2D(velocitySampler, pos - float2(0, texelSize.y)).xy;
    float2 vB = tex2D(velocitySampler, pos + float2(0, texelSize.y)).xy;

    float curl = (vR.y - vL.y) * 0.5f / texelSize.x - (vT.x - vB.x) * 0.5f / texelSize.y;

    return float4(abs(curl), curl, 0, 1);
}


float4 VorticityConfinementPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float left = tex2D(vorticitySampler, pos - float2(texelSize.x, 0)).x;
    float right = tex2D(vorticitySampler, pos + float2(texelSize.x, 0)).x;
    float top = tex2D(vorticitySampler, pos - float2(0, texelSize.y)).x;
    float bottom = tex2D(vorticitySampler, pos + float2(0, texelSize.y)).x;

    float2 gradient = float2(right - left, bottom - top);
    gradient = normalize(gradient + 1e-5); 

    float curl = tex2D(vorticitySampler, pos).y;

    float2 force = vorticityScale * float2(gradient.y, -gradient.x) * curl;

    float2 velocity = tex2D(velocitySampler, pos).xy;
    velocity += force * timeStep;

    return float4(velocity, 0, 1);
}

float4 IgnitionPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float fuel = tex2D(fuelSampler, pos).r;
    float temperature = tex2D(temperatureSampler, pos).r;

    float newTemperature = temperature;

    if (fuel > minFuelThreshold && temperature >= ignitionTemperature)
    {
        newTemperature += fuelBurnTemperature * timeStep;
    }

    return float4(newTemperature, 0, 0, 1);
}

float4 CombustionDivergencePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float fuel = tex2D(fuelSampler, pos).r;
    float temperature = tex2D(temperatureSampler, pos).r;
    float divergence = tex2D(sourceSampler, pos).x;

    float newDivergence = divergence;

    if (fuel > minFuelThreshold && temperature >= ignitionTemperature)
    {
        newDivergence += combustionPressure;
    }

    return float4(newDivergence, 0, 0, 1);
}

float4 ConsumeFuelPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float fuel = tex2D(fuelSampler, pos).r;
    float temperature = tex2D(temperatureSampler, pos).r;

    if (temperature > ignitionTemperature && fuel > 0.0f)
    {
        fuel -= fuelConsumptionRate * temperature * timeStep;
        fuel = max(fuel, 0.0f);
    }

    return float4(fuel, 0, 0, 1);
}

float4 RadiancePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float temperature = tex2D(temperatureSampler, pos).r;

    float newTemperature;

    float a = 1/ pow(temperature - ambientTemperature, 3);
    float b = (3 * coolingRate * timeStep) / pow (maxTemperature - ambientTemperature, 4);

    newTemperature = ambientTemperature + pow(a + b, -1.0/3.0);

    if(newTemperature < ambientTemperature)
    {
        newTemperature = ambientTemperature;
    }   

    return float4(newTemperature, 0, 0, 1);
}   

// Boundary condition pixel shader as described in GPU Gems Chapter 38
float4 BoundaryPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    
    // Sample from the interior cell (offset by boundaryOffset)
    float2 interiorPos = pos + boundaryOffset * texelSize;
    float4 interiorValue = tex2D(sourceSampler, interiorPos);
    
    // Apply boundary scale (for velocity: -1 for no-slip, for pressure: 1 for Neumann)
    return interiorValue * boundaryScale;
}

// Copy pixel shader for preserving interior values
float4 CopyPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    return tex2D(sourceSampler, pos);
}

float4 SpreadFirePS(VertexShaderOutput input) : COLOR0
{
    float4 centerTemp = tex2D(temperatureSampler, input.TexCoord);
    float4 leftTemp = tex2D(temperatureSampler, input.TexCoord - float2(texelSize.x, 0));
    float4 rightTemp = tex2D(temperatureSampler, input.TexCoord + float2(texelSize.x, 0));
    float4 topTemp = tex2D(temperatureSampler, input.TexCoord - float2(0, texelSize.y));
    float4 bottomTemp = tex2D(temperatureSampler, input.TexCoord + float2(0, texelSize.y));

    float4 centerFuel = tex2D(fuelSampler, input.TexCoord);
    float4 leftFuel = tex2D(fuelSampler, input.TexCoord - float2(texelSize.x, 0));
    float4 rightFuel = tex2D(fuelSampler, input.TexCoord + float2(texelSize.x, 0));
    float4 topFuel = tex2D(fuelSampler, input.TexCoord - float2(0, texelSize.y));
    float4 bottomFuel = tex2D(fuelSampler, input.TexCoord + float2(0, texelSize.y));

    if (leftFuel.r > minFuelThreshold && leftTemp.r >= ignitionTemperature && centerTemp.r < ignitionTemperature && centerFuel.r > minFuelThreshold) centerTemp.r = ignitionTemperature;
    if (rightFuel.r > minFuelThreshold && rightTemp.r >= ignitionTemperature && centerTemp.r < ignitionTemperature && centerFuel.r > minFuelThreshold) centerTemp.r = ignitionTemperature;
    if (topFuel.r > minFuelThreshold && topTemp.r >= ignitionTemperature && centerTemp.r < ignitionTemperature && centerFuel.r > minFuelThreshold) centerTemp.r = ignitionTemperature;
    if (bottomFuel.r > minFuelThreshold && bottomTemp.r >= ignitionTemperature && centerTemp.r < ignitionTemperature && centerFuel.r > minFuelThreshold) centerTemp.r = ignitionTemperature;

    return centerTemp;
}

float4 BuoyancyPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float temperature = tex2D(temperatureSampler, pos).r;
    float2 velocity = tex2D(velocitySampler, pos).xy;

    float tempDiff = temperature - ambientTemperature;

    float2 buoyancyForce = (tempDiff * heatBuoyancyConstant * gravity) * float2(0, 1);

    velocity += buoyancyForce * timeStep;

    return float4(velocity, 0, 1);
}

float4 AddSmokePS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float temperature = tex2D(temperatureSampler, pos).r;
    float fuel = tex2D(fuelSampler, pos).r;
    float smoke = tex2D(smokeSampler, pos).r;

    float addedSmoke = smokeEmissionRate * timeStep;

    if (fuel > minFuelThreshold && temperature > ignitionTemperature)
    {
        smoke += addedSmoke;
    }

    return float4(smoke, 0, 0, 1);
}

float4 ObstacleToFuelPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    
    // Sample current fuel value
    float currentFuel = tex2D(sourceSampler, pos).r;
    
    // Sample obstacle value (assuming spriteObstacle texture is bound to obstacleTexture)
    float obstacleValue = tex2D(spriteObstacleSampler, pos).r;
    
    // If there's an obstacle, add fuel proportional to obstacle strength
    float addedFuel = obstacleValue * sourceStrength * timeStep;
    
    return float4(currentFuel + addedFuel, 0, 0, 1);
}


float4 DrawSolidFuelPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    
    // Sample the sprite texture
    float4 spriteValue = tex2D(spriteSampler, pos);
    float fuel = tex2D(fuelSampler, pos).r;
    
    // get grayscale value of sprite
    float spriteGray = dot(spriteValue.rgb, float3(0.299, 0.587, 0.114));

    if(spriteGray > 0.2 && fuel < minFuelThreshold)
    {
        return float4(0.1, 0, 0, 1.0);
    }

    // Otherwise, don't add any fuel
    return float4(0, 0, 0, 1.0);
}

float4 GaussianBlurHorizontalPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    float4 result = float4(0, 0, 0, 0);
    
    // Clamp kernel size to reasonable bounds (1-32)
    int kernelSize = clamp(blurKernelSize, 1, 32);
    int halfKernel = kernelSize / 2;
    
    // Calculate Gaussian weights dynamically
    float sigma = kernelSize / 6.0f; // Sigma based on kernel size
    float twoSigmaSquared = 2.0f * sigma * sigma;
    float weightSum = 0.0f;
    
    // First pass: calculate weights and accumulate
    for (int i = 0; i < kernelSize; i++)
    {
        int offset = i - halfKernel;
        float weight = exp(-(offset * offset) / twoSigmaSquared);
        weightSum += weight;
        
        float2 samplePos = pos + float2(offset * texelSize.x * blurRadius, 0);
        result += tex2D(sourceSampler, samplePos) * weight;
    }
    
    // Normalize by total weight
    result /= weightSum;
    
    return result;
}

float4 GaussianBlurVerticalPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;
    float4 result = float4(0, 0, 0, 0);
    
    // Clamp kernel size to reasonable bounds (1-32)
    int kernelSize = clamp(blurKernelSize, 1, 32);
    int halfKernel = kernelSize / 2;
    
    // Calculate Gaussian weights dynamically
    float sigma = kernelSize / 6.0f; // Sigma based on kernel size
    float twoSigmaSquared = 2.0f * sigma * sigma;
    float weightSum = 0.0f;
    
    // First pass: calculate weights and accumulate
    for (int i = 0; i < kernelSize; i++)
    {
        int offset = i - halfKernel;
        float weight = exp(-(offset * offset) / twoSigmaSquared);
        weightSum += weight;
        
        float2 samplePos = pos + float2(0, offset * texelSize.y * blurRadius);
        result += tex2D(sourceSampler, samplePos) * weight;
    }
    
    // Normalize by total weight
    result /= weightSum;
    
    return result;
}

float4 VelocityDampingPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float2 velocity = tex2D(velocitySampler, pos).xy;

    velocity *= exp(-velocityDampingCoefficient * timeStep);

    return float4(velocity, 0, 1);
}

float4 ClampVelocityPS(VertexShaderOutput input) : COLOR0
{
    float2 pos = input.TexCoord;

    float2 velocity = tex2D(velocitySampler, pos).xy;

    float maxVelocity = 0.5 * texelSize.x /  timeStep; // Define a maximum velocity threshold

    float speed = length(velocity);
    if (speed > maxVelocity)
    {
        velocity = normalize(velocity) * maxVelocity;
    }

    return float4(velocity, 0, 1);
}

technique Advect
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL AdvectPS();
    }
}

technique Diffuse
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL DiffusePS();
    }
}

technique Visualize
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL VisualizePS();
    }
}

technique Project
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ProjectPS();
    }
}

technique ComputeDivergence
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ComputeDivergencePS();
    }
}

technique Clamp
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ClampPS();
    }
}

technique AddValue
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL AddValuePS();
    }
}

technique AddValueDiffuse
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL AddValueDiffusePS();
    }
}

technique SetValue
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL SetValuePS();
    }
}

technique JacobiPressure
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL JacobiPressurePS();
    }
}

technique ComputeVorticity
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ComputeVorticityPS();
    }
}

technique VorticityConfinement
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL VorticityConfinementPS();
    }
}

technique Ignition
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL IgnitionPS();
    }
}

technique ConsumeFuel
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ConsumeFuelPS();
    }
}

technique Radiance
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL RadiancePS();
    }
}

technique CombustionDivergence
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL CombustionDivergencePS();
    }
}

technique Boundary
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL BoundaryPS();
    }
}

technique Copy
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL CopyPS();
    }
}

technique SpreadFire
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL SpreadFirePS();
    }
}

technique Buoyancy
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL BuoyancyPS();
    }
}

technique AddSmoke
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL AddSmokePS();
    }
}

technique DrawSolidFuel
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL DrawSolidFuelPS();
    }
}

technique GaussianBlurHorizontal
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL GaussianBlurHorizontalPS();
    }
}

technique GaussianBlurVertical
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL GaussianBlurVerticalPS();
    }
}

technique ObstacleToFuel
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ObstacleToFuelPS();
    }
}

technique VisualizeVelocity
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL VisualizeVelocityPS();
    }
}

technique ApplyGravity
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ApplyGravityPS();
    }
}

technique VelocityDamping
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL VelocityDampingPS();
    }
}

technique ClampVelocity
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL ClampVelocityPS();
    }
}