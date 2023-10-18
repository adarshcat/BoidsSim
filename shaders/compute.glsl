#[compute]
#version 460

layout(local_size_x = 20, local_size_y = 20, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict buffer SettingsDataBuffer {
    int gridPxSize;
    int dimX;
    int dimY;
    int cellSize;
    int boundsX;
    int boundsY;
}
settings;

layout(set = 0, binding = 1, std430) restrict buffer GridDataBuffer {
    int[] data;
}
gridData;

layout(set = 0, binding = 2, std430) restrict buffer ParticleDataBuffer {
    float[] data;
}
particleData;

layout(set = 0, binding = 3, std430) restrict buffer MouseDataBuffer {
    float x;
    float y;
}
mouseData;

const float maxSpeed = 4;

const float alignForce = 0.8;
const int alignRad = 20; //25

const float sepForce = 0.05;
const int sepRad = 6;

const float cohesionForce = 0.1;
const int cohesionRad = 5;

int getParticleIndex(ivec2 coord, int offset){
    int arrIndex = coord.y*settings.dimX*settings.cellSize + coord.x*settings.cellSize;
    return gridData.data[arrIndex+offset]*4 - 4;
}

void setPos(int index, vec2 pos){
    particleData.data[index] = pos.x;
    particleData.data[index+1] = pos.y;
}

vec2 getPos(int index){
    return vec2(particleData.data[index], particleData.data[index+1]);
}

void setVel(int index, vec2 vel){
    particleData.data[index+2] = vel.x;
    particleData.data[index+3] = vel.y;
}

vec2 getVel(int index){
    return vec2(particleData.data[index+2], particleData.data[index+3]);
}

void limit(inout vec2 vector, float maxMag){
    float currentMag = length(vector);
    if (currentMag < maxMag) return;

    vector /= currentMag;
    vector *= maxMag;
}

void applyForce(inout vec2 acc, vec2 force){
    acc += force;
}

void alignBoids(ivec2 coord, int currIndex, vec2 pos, vec2 vel, inout vec2 acc){
    int gridRad = int(ceil(float(max(cohesionRad, max(alignRad, sepRad)))/float(settings.gridPxSize)));

    int startX = max(0, coord.x-gridRad);
    int endX = min(settings.dimX-1, coord.x+gridRad);

    int startY = max(0, coord.y-gridRad);
    int endY = min(settings.dimY-1, coord.y+gridRad);

    vec2 alignVel = vec2(0.0, 0.0);
    int alignTotal = 0;

    vec2 sepVel = vec2(0.0, 0.0);
    int sepTotal = 0;

    vec2 cohesionPos = vec2(0.0, 0.0);
    int cohesionTotal = 0;

    for (int i=startX; i<=endX; i++){
        for (int j=startY; j<=endY; j++){
            for (int k=0; k<settings.cellSize; k++){
                int otherPartIndex = getParticleIndex(ivec2(i, j), k);
                if (otherPartIndex == -1){
                    break;
                }
                if (otherPartIndex == currIndex){
                    continue;
                }
                vec2 otherPos = getPos(otherPartIndex);
                vec2 posRelative = normalize(otherPos - pos);
                if (dot(posRelative, vel) < 0){
                    continue;
                }

                float distSq = (otherPos.x-pos.x)*(otherPos.x-pos.x) + (otherPos.y-pos.y)*(otherPos.y-pos.y);

                if (distSq < alignRad*alignRad) {
                    vec2 otherVel = getVel(otherPartIndex);
                    alignVel += otherVel;
                    alignTotal += 1;
                }

                if (distSq < sepRad*sepRad){
                    vec2 diff = pos - otherPos;
                    sepVel += diff;
                    sepTotal += 1;
                }

                if (distSq < cohesionRad*cohesionRad){
                    cohesionPos += getPos(otherPartIndex);
                    cohesionTotal += 1;
                }
            }
        }
    }

    if (alignTotal != 0){
        alignVel /= alignTotal;
        alignVel *= alignForce;

        applyForce(acc, alignVel);
    }

    if (sepTotal != 0 && true){
        sepVel /= sepTotal;
        sepVel *= sepForce*10.0;

        applyForce(acc, sepVel);
    }

    if (cohesionTotal != 0 && true){
        cohesionPos /= cohesionTotal;
        vec2 steer = cohesionPos - pos;
        steer *= cohesionForce;

        applyForce(acc, steer);
    }
}

void main() {
    const ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
    const int arrIndex = coord.y*settings.dimX*settings.cellSize + coord.x*settings.cellSize;

    for (int i=arrIndex; i<arrIndex+settings.cellSize; i++){
        int particleIndex = gridData.data[i]*4 - 4;
        if (particleIndex == -1){
            break;
        }

        vec2 vel = getVel(particleIndex);
        vec2 pos = getPos(particleIndex);

        //All that physics stuff goes here--
        vec2 acc = vec2(0.0, 0.0);
        alignBoids(coord, particleIndex, pos, vel, acc);

        if (mouseData.x != 696969.0){
            vec2 mouseRelative = vec2(mouseData.x, mouseData.y) - pos;
            float mouseRelLen = length(mouseRelative);
            if (mouseRelLen < 500){
                acc += (mouseRelative/mouseRelLen) * 0.25;
            }
        }

        vel += acc;
        limit(vel, maxSpeed);
        setVel(particleIndex, vel);

        vec2 newPos = pos + vel;

        // Wrap around the bounds
        if (newPos.x > settings.boundsX) newPos.x = 1;
        else if (newPos.x < 1) newPos.x = settings.boundsX;

        if (newPos.y > settings.boundsY) newPos.y = 1;
        else if (newPos.y < 1) newPos.y = settings.boundsY;

        setPos(particleIndex, newPos);
    }
}

/*void updateCell(int currentIndex, vec2 pos, ivec2 coord, int index){
    ivec2 newGridIndex = ivec2(pos/settings.gridPxSize);
    if (newGridIndex.x == coord.x && newGridIndex.y == coord.y){
        return;
    }

    gridData.data[index] = 0;
    int newIndex = newGridIndex.y*settings.dimX*settings.cellSize + newGridIndex.x*settings.cellSize;
    for (int i=newIndex; i<newIndex+settings.cellSize; i++){
        if (gridData.data[i] == 0){
            gridData.data[i] = currentIndex+1;
            break;
        }
    }
}*/