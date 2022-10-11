#version 450

layout(set = 0, binding = 0) uniform GlobalVar {
    mat4 view;
    mat4 proj;
    mat4 viewProj;
};

layout(set = 0, binding = 1) uniform MaterialVar {
    vec4 diffuseColor;
};

layout(location = 0) in vec4 fragColor;

layout(location = 0) out vec4 outColor;

void main() {
    outColor = fragColor * diffuseColor;
}