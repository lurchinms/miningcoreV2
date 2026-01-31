#include "chacha20.h"

#if defined(__x86_64__)
    #include <memory.h>

    #if defined(__AVX512F__) || defined (__AVX2__) || defined(__SSE2__)
      #include <immintrin.h>
    #endif

    static const int32_t KeyDataSize = 48;
    static const int32_t rounds = 20;

    static const uint32_t ConstState[4] = { 1634760805, 857760878, 2036477234, 1797285236 };  //"expand 32-byte k";;

    void ChaCha20SetKey(uint8_t* state, const uint8_t* Key)
    {
            memcpy(state, Key, 32);
    }

    void ChaCha20SetNonce(uint8_t* state, const uint8_t* Nonce)
    {
            memcpy(state + 36, Nonce, 12);
    }

    void ChaCha20SetCtr(uint8_t* state, const uint8_t* Ctr)
    {
            memcpy(state + 32, Ctr, 4);
    }


    void ChaCha20IncrementNonce(uint8_t* state)
    {
            uint32_t* State32bits = (uint32_t*)state;
            State32bits[8] = 0; //reset counter
            ++State32bits[9];
            if (State32bits[9] == 0)
            {
                    ++State32bits[10];
                    if (State32bits[10] == 0) ++State32bits[11];
            }
    }

    #if defined(__AVX512F__)

        inline void PartialXor(const __m512i val, uint8_t* Src, uint8_t* Dest, uint64_t Size)
        {
                _Alignas(64) uint8_t BuffForPartialOp[64];
                memcpy(BuffForPartialOp, Src, Size);
                _mm512_storeu_si512((__m512i*)(BuffForPartialOp), _mm512_xor_si512(val, _mm512_loadu_si512((const __m512i*)BuffForPartialOp)));
                memcpy(Dest, BuffForPartialOp, Size);
        }

        inline void PartialStore(const __m512i val, uint8_t* Dest, uint64_t Size)
        {
                _Alignas(64) uint8_t BuffForPartialOp[64];
                _mm512_storeu_si512((__m512i*)(BuffForPartialOp), val);
                memcpy(Dest, BuffForPartialOp, Size);
        }

        void ChaCha20EncryptBytes(uint8_t *state, uint8_t *In, uint8_t *Out, uint64_t Size, int rounds)
        {
          uint8_t *CurrentIn = In;
          uint8_t *CurrentOut = Out;

          uint64_t FullBlocksCount = Size / 1024;
          uint64_t RemainingBytes = Size % 1024;

          const __m512i state0 = _mm512_broadcast_i32x4(_mm_set_epi32(
              1797285236, 2036477234, 857760878, 1634760805)); //"expand 32-byte k"
          const __m512i state1 =
              _mm512_broadcast_i32x4(_mm_loadu_si128((const __m128i *)(state)));
          const __m512i state2 =
              _mm512_broadcast_i32x4(_mm_loadu_si128((const __m128i *)(state + 16)));

          // AVX2 for partial blocks
          const __m256i state0_r = _mm256_broadcastsi128_si256(_mm_set_epi32(
              1797285236, 2036477234, 857760878, 1634760805)); //"expand 32-byte k"
          const __m256i state1_r =
              _mm256_broadcastsi128_si256(_mm_load_si128((const __m128i *)(state)));
          const __m256i state2_r = _mm256_broadcastsi128_si256(
              _mm_load_si128((const __m128i *)(state + 16)));

          // end of AVX2 definitions

          // __m512i state3_r = _mm512_broadcast_i32x4(
          //     _mm_load_si128((const __m128i*)(state + 32)));

          __m512i CTR0 = _mm512_set_epi64(0, 0, 0, 4, 0, 8, 0, 12);
          const __m512i CTR1 = _mm512_set_epi64(0, 1, 0, 5, 0, 9, 0, 13);
          const __m512i CTR2 = _mm512_set_epi64(0, 2, 0, 6, 0, 10, 0, 14);
          const __m512i CTR3 = _mm512_set_epi64(0, 3, 0, 7, 0, 11, 0, 15);

          // permutation indexes for results
          const __m512i P1 = _mm512_set_epi64(13, 12, 5, 4, 9, 8, 1, 0);
          const __m512i P2 = _mm512_set_epi64(15, 14, 7, 6, 11, 10, 3, 2);
          const __m512i P3 = _mm512_set_epi64(11, 10, 9, 8, 3, 2, 1, 0);
          const __m512i P4 = _mm512_set_epi64(15, 14, 13, 12, 7, 6, 5, 4);

          __m512i T1;
          __m512i T2;
          __m512i T3;
          __m512i T4;

          if (FullBlocksCount > 0)
          {
            for (uint64_t n = 0; n < FullBlocksCount; n++)
            {
              const __m512i state3 = _mm512_broadcast_i32x4(
                  _mm_loadu_si128((const __m128i *)(state + 32)));

              __m512i X0_0 = state0;
              __m512i X0_1 = state1;
              __m512i X0_2 = state2;
              __m512i X0_3 = _mm512_add_epi32(state3, CTR0);

              __m512i X1_0 = state0;
              __m512i X1_1 = state1;
              __m512i X1_2 = state2;
              __m512i X1_3 = _mm512_add_epi32(state3, CTR1);

              __m512i X2_0 = state0;
              __m512i X2_1 = state1;
              __m512i X2_2 = state2;
              __m512i X2_3 = _mm512_add_epi32(state3, CTR2);

              __m512i X3_0 = state0;
              __m512i X3_1 = state1;
              __m512i X3_2 = state2;
              __m512i X3_3 = _mm512_add_epi32(state3, CTR3);

              for (int i = rounds; i > 0; i -= 2)
              {
                X0_0 = _mm512_add_epi32(X0_0, X0_1);
                X1_0 = _mm512_add_epi32(X1_0, X1_1);
                X2_0 = _mm512_add_epi32(X2_0, X2_1);
                X3_0 = _mm512_add_epi32(X3_0, X3_1);

                X0_3 = _mm512_xor_si512(X0_3, X0_0);
                X1_3 = _mm512_xor_si512(X1_3, X1_0);
                X2_3 = _mm512_xor_si512(X2_3, X2_0);
                X3_3 = _mm512_xor_si512(X3_3, X3_0);

                X0_3 = _mm512_rol_epi32(X0_3, 16);
                X1_3 = _mm512_rol_epi32(X1_3, 16);
                X2_3 = _mm512_rol_epi32(X2_3, 16);
                X3_3 = _mm512_rol_epi32(X3_3, 16);

                //

                X0_2 = _mm512_add_epi32(X0_2, X0_3);
                X1_2 = _mm512_add_epi32(X1_2, X1_3);
                X2_2 = _mm512_add_epi32(X2_2, X2_3);
                X3_2 = _mm512_add_epi32(X3_2, X3_3);

                X0_1 = _mm512_xor_si512(X0_1, X0_2);
                X1_1 = _mm512_xor_si512(X1_1, X1_2);
                X2_1 = _mm512_xor_si512(X2_1, X2_2);
                X3_1 = _mm512_xor_si512(X3_1, X3_2);

                X0_1 = _mm512_rol_epi32(X0_1, 12);
                X1_1 = _mm512_rol_epi32(X1_1, 12);
                X2_1 = _mm512_rol_epi32(X2_1, 12);
                X3_1 = _mm512_rol_epi32(X3_1, 12);

                //

                X0_0 = _mm512_add_epi32(X0_0, X0_1);
                X1_0 = _mm512_add_epi32(X1_0, X1_1);
                X2_0 = _mm512_add_epi32(X2_0, X2_1);
                X3_0 = _mm512_add_epi32(X3_0, X3_1);

                X0_3 = _mm512_xor_si512(X0_3, X0_0);
                X1_3 = _mm512_xor_si512(X1_3, X1_0);
                X2_3 = _mm512_xor_si512(X2_3, X2_0);
                X3_3 = _mm512_xor_si512(X3_3, X3_0);

                X0_3 = _mm512_rol_epi32(X0_3, 8);
                X1_3 = _mm512_rol_epi32(X1_3, 8);
                X2_3 = _mm512_rol_epi32(X2_3, 8);
                X3_3 = _mm512_rol_epi32(X3_3, 8);

                //

                X0_2 = _mm512_add_epi32(X0_2, X0_3);
                X1_2 = _mm512_add_epi32(X1_2, X1_3);
                X2_2 = _mm512_add_epi32(X2_2, X2_3);
                X3_2 = _mm512_add_epi32(X3_2, X3_3);

                X0_1 = _mm512_xor_si512(X0_1, X0_2);
                X1_1 = _mm512_xor_si512(X1_1, X1_2);
                X2_1 = _mm512_xor_si512(X2_1, X2_2);
                X3_1 = _mm512_xor_si512(X3_1, X3_2);

                X0_1 = _mm512_rol_epi32(X0_1, 7);
                X1_1 = _mm512_rol_epi32(X1_1, 7);
                X2_1 = _mm512_rol_epi32(X2_1, 7);
                X3_1 = _mm512_rol_epi32(X3_1, 7);

                //

                X0_1 = _mm512_shuffle_epi32(X0_1, _MM_SHUFFLE(0, 3, 2, 1));
                X0_2 = _mm512_shuffle_epi32(X0_2, _MM_SHUFFLE(1, 0, 3, 2));
                X0_3 = _mm512_shuffle_epi32(X0_3, _MM_SHUFFLE(2, 1, 0, 3));

                X1_1 = _mm512_shuffle_epi32(X1_1, _MM_SHUFFLE(0, 3, 2, 1));
                X1_2 = _mm512_shuffle_epi32(X1_2, _MM_SHUFFLE(1, 0, 3, 2));
                X1_3 = _mm512_shuffle_epi32(X1_3, _MM_SHUFFLE(2, 1, 0, 3));

                X2_1 = _mm512_shuffle_epi32(X2_1, _MM_SHUFFLE(0, 3, 2, 1));
                X2_2 = _mm512_shuffle_epi32(X2_2, _MM_SHUFFLE(1, 0, 3, 2));
                X2_3 = _mm512_shuffle_epi32(X2_3, _MM_SHUFFLE(2, 1, 0, 3));

                X3_1 = _mm512_shuffle_epi32(X3_1, _MM_SHUFFLE(0, 3, 2, 1));
                X3_2 = _mm512_shuffle_epi32(X3_2, _MM_SHUFFLE(1, 0, 3, 2));
                X3_3 = _mm512_shuffle_epi32(X3_3, _MM_SHUFFLE(2, 1, 0, 3));

                //

                X0_0 = _mm512_add_epi32(X0_0, X0_1);
                X1_0 = _mm512_add_epi32(X1_0, X1_1);
                X2_0 = _mm512_add_epi32(X2_0, X2_1);
                X3_0 = _mm512_add_epi32(X3_0, X3_1);

                X0_3 = _mm512_xor_si512(X0_3, X0_0);
                X1_3 = _mm512_xor_si512(X1_3, X1_0);
                X2_3 = _mm512_xor_si512(X2_3, X2_0);
                X3_3 = _mm512_xor_si512(X3_3, X3_0);

                X0_3 = _mm512_rol_epi32(X0_3, 16);
                X1_3 = _mm512_rol_epi32(X1_3, 16);
                X2_3 = _mm512_rol_epi32(X2_3, 16);
                X3_3 = _mm512_rol_epi32(X3_3, 16);

                //

                X0_2 = _mm512_add_epi32(X0_2, X0_3);
                X1_2 = _mm512_add_epi32(X1_2, X1_3);
                X2_2 = _mm512_add_epi32(X2_2, X2_3);
                X3_2 = _mm512_add_epi32(X3_2, X3_3);

                X0_1 = _mm512_xor_si512(X0_1, X0_2);
                X1_1 = _mm512_xor_si512(X1_1, X1_2);
                X2_1 = _mm512_xor_si512(X2_1, X2_2);
                X3_1 = _mm512_xor_si512(X3_1, X3_2);

                X0_1 = _mm512_rol_epi32(X0_1, 12);
                X1_1 = _mm512_rol_epi32(X1_1, 12);
                X2_1 = _mm512_rol_epi32(X2_1, 12);
                X3_1 = _mm512_rol_epi32(X3_1, 12);

                //

                X0_0 = _mm512_add_epi32(X0_0, X0_1);
                X1_0 = _mm512_add_epi32(X1_0, X1_1);
                X2_0 = _mm512_add_epi32(X2_0, X2_1);
                X3_0 = _mm512_add_epi32(X3_0, X3_1);

                X0_3 = _mm512_xor_si512(X0_3, X0_0);
                X1_3 = _mm512_xor_si512(X1_3, X1_0);
                X2_3 = _mm512_xor_si512(X2_3, X2_0);
                X3_3 = _mm512_xor_si512(X3_3, X3_0);

                X0_3 = _mm512_rol_epi32(X0_3, 8);
                X1_3 = _mm512_rol_epi32(X1_3, 8);
                X2_3 = _mm512_rol_epi32(X2_3, 8);
                X3_3 = _mm512_rol_epi32(X3_3, 8);

                //

                X0_2 = _mm512_add_epi32(X0_2, X0_3);
                X1_2 = _mm512_add_epi32(X1_2, X1_3);
                X2_2 = _mm512_add_epi32(X2_2, X2_3);
                X3_2 = _mm512_add_epi32(X3_2, X3_3);

                X0_1 = _mm512_xor_si512(X0_1, X0_2);
                X1_1 = _mm512_xor_si512(X1_1, X1_2);
                X2_1 = _mm512_xor_si512(X2_1, X2_2);
                X3_1 = _mm512_xor_si512(X3_1, X3_2);

                X0_1 = _mm512_rol_epi32(X0_1, 7);
                X1_1 = _mm512_rol_epi32(X1_1, 7);
                X2_1 = _mm512_rol_epi32(X2_1, 7);
                X3_1 = _mm512_rol_epi32(X3_1, 7);

                //

                X0_1 = _mm512_shuffle_epi32(X0_1, _MM_SHUFFLE(2, 1, 0, 3));
                X0_2 = _mm512_shuffle_epi32(X0_2, _MM_SHUFFLE(1, 0, 3, 2));
                X0_3 = _mm512_shuffle_epi32(X0_3, _MM_SHUFFLE(0, 3, 2, 1));

                X1_1 = _mm512_shuffle_epi32(X1_1, _MM_SHUFFLE(2, 1, 0, 3));
                X1_2 = _mm512_shuffle_epi32(X1_2, _MM_SHUFFLE(1, 0, 3, 2));
                X1_3 = _mm512_shuffle_epi32(X1_3, _MM_SHUFFLE(0, 3, 2, 1));

                X2_1 = _mm512_shuffle_epi32(X2_1, _MM_SHUFFLE(2, 1, 0, 3));
                X2_2 = _mm512_shuffle_epi32(X2_2, _MM_SHUFFLE(1, 0, 3, 2));
                X2_3 = _mm512_shuffle_epi32(X2_3, _MM_SHUFFLE(0, 3, 2, 1));

                X3_1 = _mm512_shuffle_epi32(X3_1, _MM_SHUFFLE(2, 1, 0, 3));
                X3_2 = _mm512_shuffle_epi32(X3_2, _MM_SHUFFLE(1, 0, 3, 2));
                X3_3 = _mm512_shuffle_epi32(X3_3, _MM_SHUFFLE(0, 3, 2, 1));
              }

              X0_0 = _mm512_add_epi32(X0_0, state0);
              X0_1 = _mm512_add_epi32(X0_1, state1);
              X0_2 = _mm512_add_epi32(X0_2, state2);
              X0_3 = _mm512_add_epi32(X0_3, state3);
              X0_3 = _mm512_add_epi32(X0_3, CTR0);

              X1_0 = _mm512_add_epi32(X1_0, state0);
              X1_1 = _mm512_add_epi32(X1_1, state1);
              X1_2 = _mm512_add_epi32(X1_2, state2);
              X1_3 = _mm512_add_epi32(X1_3, state3);
              X1_3 = _mm512_add_epi32(X1_3, CTR1);

              X2_0 = _mm512_add_epi32(X2_0, state0);
              X2_1 = _mm512_add_epi32(X2_1, state1);
              X2_2 = _mm512_add_epi32(X2_2, state2);
              X2_3 = _mm512_add_epi32(X2_3, state3);
              X2_3 = _mm512_add_epi32(X2_3, CTR2);

              X3_0 = _mm512_add_epi32(X3_0, state0);
              X3_1 = _mm512_add_epi32(X3_1, state1);
              X3_2 = _mm512_add_epi32(X3_2, state2);
              X3_3 = _mm512_add_epi32(X3_3, state3);
              X3_3 = _mm512_add_epi32(X3_3, CTR3);

              // permutation indexes
              __m512i idx1 = _mm512_set_epi64(15, 14, 7, 6, 15, 14, 7, 6);
              __m512i idx2 = _mm512_set_epi64(13, 12, 5, 4, 13, 12, 5, 4);
              __m512i idx3 = _mm512_set_epi64(11, 10, 3, 2, 11, 10, 3, 2);
              __m512i idx4 = _mm512_set_epi64(9, 8, 1, 0, 9, 8, 1, 0);

              // Blend the results
              __m512i X0_0F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X0_0, idx1, X0_1),
                  _mm512_permutex2var_epi64(X0_2, idx1, X0_3));
              __m512i X0_1F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X1_0, idx1, X1_1),
                  _mm512_permutex2var_epi64(X1_2, idx1, X1_3));
              __m512i X0_2F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X2_0, idx1, X2_1),
                  _mm512_permutex2var_epi64(X2_2, idx1, X2_3));
              __m512i X0_3F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X3_0, idx1, X3_1),
                  _mm512_permutex2var_epi64(X3_2, idx1, X3_3));

              //

              __m512i X1_0F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X0_0, idx2, X0_1),
                  _mm512_permutex2var_epi64(X0_2, idx2, X0_3));
              __m512i X1_1F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X1_0, idx2, X1_1),
                  _mm512_permutex2var_epi64(X1_2, idx2, X1_3));
              __m512i X1_2F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X2_0, idx2, X2_1),
                  _mm512_permutex2var_epi64(X2_2, idx2, X2_3));
              __m512i X1_3F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X3_0, idx2, X3_1),
                  _mm512_permutex2var_epi64(X3_2, idx2, X3_3));

              //

              __m512i X2_0F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X0_0, idx3, X0_1),
                  _mm512_permutex2var_epi64(X0_2, idx3, X0_3));
              __m512i X2_1F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X1_0, idx3, X1_1),
                  _mm512_permutex2var_epi64(X1_2, idx3, X1_3));
              __m512i X2_2F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X2_0, idx3, X2_1),
                  _mm512_permutex2var_epi64(X2_2, idx3, X2_3));
              __m512i X2_3F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X3_0, idx3, X3_1),
                  _mm512_permutex2var_epi64(X3_2, idx3, X3_3));

              //

              __m512i X3_0F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X0_0, idx4, X0_1),
                  _mm512_permutex2var_epi64(X0_2, idx4, X0_3));
              __m512i X3_1F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X1_0, idx4, X1_1),
                  _mm512_permutex2var_epi64(X1_2, idx4, X1_3));
              __m512i X3_2F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X2_0, idx4, X2_1),
                  _mm512_permutex2var_epi64(X2_2, idx4, X2_3));
              __m512i X3_3F = _mm512_mask_blend_epi64(
                  0xF0,
                  _mm512_permutex2var_epi64(X3_0, idx4, X3_1),
                  _mm512_permutex2var_epi64(X3_2, idx4, X3_3));

              if (In)
              {
                T1 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 0 * 64));
                T2 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 1 * 64));
                T3 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 2 * 64));
                T4 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 3 * 64));

                T1 = _mm512_xor_si512(T1, X0_0F);
                T2 = _mm512_xor_si512(T2, X0_1F);
                T3 = _mm512_xor_si512(T3, X0_2F);
                T4 = _mm512_xor_si512(T4, X0_3F);

                _mm512_storeu_si512(CurrentOut + 0 * 64, T1);
                _mm512_storeu_si512(CurrentOut + 1 * 64, T2);
                _mm512_storeu_si512(CurrentOut + 2 * 64, T3);
                _mm512_storeu_si512(CurrentOut + 3 * 64, T4);

                T1 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 4 * 64));
                T2 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 5 * 64));
                T3 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 6 * 64));
                T4 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 7 * 64));

                T1 = _mm512_xor_si512(T1, X1_0F);
                T2 = _mm512_xor_si512(T2, X1_1F);
                T3 = _mm512_xor_si512(T3, X1_2F);
                T4 = _mm512_xor_si512(T4, X1_3F);

                _mm512_storeu_si512(CurrentOut + 4 * 64, T1);
                _mm512_storeu_si512(CurrentOut + 5 * 64, T2);
                _mm512_storeu_si512(CurrentOut + 6 * 64, T3);
                _mm512_storeu_si512(CurrentOut + 7 * 64, T4);

                T1 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 8 * 64));
                T2 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 9 * 64));
                T3 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 10 * 64));
                T4 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 11 * 64));

                T1 = _mm512_xor_si512(T1, X2_0F);
                T2 = _mm512_xor_si512(T2, X2_1F);
                T3 = _mm512_xor_si512(T3, X2_2F);
                T4 = _mm512_xor_si512(T4, X2_3F);

                _mm512_storeu_si512(CurrentOut + 8 * 64, T1);
                _mm512_storeu_si512(CurrentOut + 9 * 64, T2);
                _mm512_storeu_si512(CurrentOut + 10 * 64, T3);
                _mm512_storeu_si512(CurrentOut + 11 * 64, T4);

                T1 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 12 * 64));
                T2 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 13 * 64));
                T3 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 14 * 64));
                T4 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 15 * 64));

                T1 = _mm512_xor_si512(T1, X3_0F);
                T2 = _mm512_xor_si512(T2, X3_1F);
                T3 = _mm512_xor_si512(T3, X3_2F);
                T4 = _mm512_xor_si512(T4, X3_3F);

                _mm512_storeu_si512(CurrentOut + 12 * 64, T1);
                _mm512_storeu_si512(CurrentOut + 13 * 64, T2);
                _mm512_storeu_si512(CurrentOut + 14 * 64, T3);
                _mm512_storeu_si512(CurrentOut + 15 * 64, T4);
              }
              else
              {
                _mm512_storeu_si512(CurrentOut + 0 * 64, X0_0F);
                _mm512_storeu_si512(CurrentOut + 1 * 64, X0_1F);
                _mm512_storeu_si512(CurrentOut + 2 * 64, X0_2F);
                _mm512_storeu_si512(CurrentOut + 3 * 64, X0_3F);

                _mm512_storeu_si512(CurrentOut + 4 * 64, X1_0F);
                _mm512_storeu_si512(CurrentOut + 5 * 64, X1_1F);
                _mm512_storeu_si512(CurrentOut + 6 * 64, X1_2F);
                _mm512_storeu_si512(CurrentOut + 7 * 64, X1_3F);

                _mm512_storeu_si512(CurrentOut + 8 * 64, X2_0F);
                _mm512_storeu_si512(CurrentOut + 9 * 64, X2_1F);
                _mm512_storeu_si512(CurrentOut + 10 * 64, X2_2F);
                _mm512_storeu_si512(CurrentOut + 11 * 64, X2_3F);

                _mm512_storeu_si512(CurrentOut + 12 * 64, X3_0F);
                _mm512_storeu_si512(CurrentOut + 13 * 64, X3_1F);
                _mm512_storeu_si512(CurrentOut + 14 * 64, X3_2F);
                _mm512_storeu_si512(CurrentOut + 15 * 64, X3_3F);
              }

              ChaCha20AddCounter(state, 16);
              if (CurrentIn)
                CurrentIn += 1024;
              CurrentOut += 1024;
            }
          }

          if (RemainingBytes == 0)
            return;
          // now computing rest in 4-blocks cycle

          CTR0 = _mm512_set_epi64(0, 0, 0, 1, 0, 2, 0, 3);

          while (1)
          {
            const __m512i state3 = _mm512_broadcast_i32x4(
                _mm_load_si128((const __m128i *)(state + 32)));

            __m512i X0_0 = state0;
            __m512i X0_1 = state1;
            __m512i X0_2 = state2;
            __m512i X0_3 = _mm512_add_epi32(state3, CTR0);

            for (int i = rounds; i > 0; i -= 2)
            {
              X0_0 = _mm512_add_epi32(X0_0, X0_1);

              X0_3 = _mm512_xor_si512(X0_3, X0_0);

              X0_3 = _mm512_rol_epi32(X0_3, 16);

              X0_2 = _mm512_add_epi32(X0_2, X0_3);

              X0_1 = _mm512_xor_si512(X0_1, X0_2);

              X0_1 = _mm512_rol_epi32(X0_1, 12);

              X0_0 = _mm512_add_epi32(X0_0, X0_1);

              X0_3 = _mm512_xor_si512(X0_3, X0_0);

              X0_3 = _mm512_rol_epi32(X0_3, 8);

              X0_2 = _mm512_add_epi32(X0_2, X0_3);

              X0_1 = _mm512_xor_si512(X0_1, X0_2);

              X0_1 = _mm512_rol_epi32(X0_1, 7);

              X0_1 = _mm512_shuffle_epi32(X0_1, _MM_SHUFFLE(0, 3, 2, 1));
              X0_2 = _mm512_shuffle_epi32(X0_2, _MM_SHUFFLE(1, 0, 3, 2));
              X0_3 = _mm512_shuffle_epi32(X0_3, _MM_SHUFFLE(2, 1, 0, 3));

              X0_0 = _mm512_add_epi32(X0_0, X0_1);

              X0_3 = _mm512_xor_si512(X0_3, X0_0);

              X0_3 = _mm512_rol_epi32(X0_3, 16);

              X0_2 = _mm512_add_epi32(X0_2, X0_3);

              X0_1 = _mm512_xor_si512(X0_1, X0_2);

              X0_1 = _mm512_rol_epi32(X0_1, 12);

              X0_0 = _mm512_add_epi32(X0_0, X0_1);

              X0_3 = _mm512_xor_si512(X0_3, X0_0);

              X0_3 = _mm512_rol_epi32(X0_3, 8);

              X0_2 = _mm512_add_epi32(X0_2, X0_3);

              X0_1 = _mm512_xor_si512(X0_1, X0_2);

              X0_1 = _mm512_rol_epi32(X0_1, 7);

              X0_1 = _mm512_shuffle_epi32(X0_1, _MM_SHUFFLE(2, 1, 0, 3));
              X0_2 = _mm512_shuffle_epi32(X0_2, _MM_SHUFFLE(1, 0, 3, 2));
              X0_3 = _mm512_shuffle_epi32(X0_3, _MM_SHUFFLE(0, 3, 2, 1));
            }

            X0_0 = _mm512_add_epi32(X0_0, state0);
            X0_1 = _mm512_add_epi32(X0_1, state1);
            X0_2 = _mm512_add_epi32(X0_2, state2);
            X0_3 = _mm512_add_epi32(X0_3, state3);
            X0_3 = _mm512_add_epi32(X0_3, CTR0);

            __m512i idx1 = _mm512_set_epi64(15, 14, 7, 6, 15, 14, 7, 6);
            __m512i idx2 = _mm512_set_epi64(13, 12, 5, 4, 13, 12, 5, 4);
            __m512i idx3 = _mm512_set_epi64(11, 10, 3, 2, 11, 10, 3, 2);
            __m512i idx4 = _mm512_set_epi64(9, 8, 1, 0, 9, 8, 1, 0);

            // Blend the results
            __m512i X0_0F = _mm512_mask_blend_epi64(
                0xF0,
                _mm512_permutex2var_epi64(X0_0, idx1, X0_1),
                _mm512_permutex2var_epi64(X0_2, idx1, X0_3));
            __m512i X0_1F = _mm512_mask_blend_epi64(
                0xF0,
                _mm512_permutex2var_epi64(X0_0, idx2, X0_1),
                _mm512_permutex2var_epi64(X0_2, idx2, X0_3));
            __m512i X0_2F = _mm512_mask_blend_epi64(
                0xF0,
                _mm512_permutex2var_epi64(X0_0, idx3, X0_1),
                _mm512_permutex2var_epi64(X0_2, idx3, X0_3));
            __m512i X0_3F = _mm512_mask_blend_epi64(
                0xF0,
                _mm512_permutex2var_epi64(X0_0, idx4, X0_1),
                _mm512_permutex2var_epi64(X0_2, idx4, X0_3));

            if (RemainingBytes >= 256)
            {
              if (In)
              {
                T1 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 0 * 64));
                T2 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 1 * 64));
                T3 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 2 * 64));
                T4 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 3 * 64));

                T1 = _mm512_xor_si512(T1, X0_0F);
                T2 = _mm512_xor_si512(T2, X0_1F);
                T3 = _mm512_xor_si512(T3, X0_2F);
                T4 = _mm512_xor_si512(T4, X0_3F);

                _mm512_storeu_si512(CurrentOut + 0 * 64, T1);
                _mm512_storeu_si512(CurrentOut + 1 * 64, T2);
                _mm512_storeu_si512(CurrentOut + 2 * 64, T3);
                _mm512_storeu_si512(CurrentOut + 3 * 64, T4);
              }
              else
              {
                _mm512_storeu_si512(CurrentOut + 0 * 64, X0_0F);
                _mm512_storeu_si512(CurrentOut + 1 * 64, X0_1F);
                _mm512_storeu_si512(CurrentOut + 2 * 64, X0_2F);
                _mm512_storeu_si512(CurrentOut + 3 * 64, X0_3F);
              }
              ChaCha20AddCounter(state, 4);
              RemainingBytes -= 256;
              if (RemainingBytes == 0)
                return;
              if (CurrentIn)
                CurrentIn += 256;
              CurrentOut += 256;
              continue;
            }
            else
            {
              if (In)
              {
                if (RemainingBytes < 64)
                {
                  PartialXor(X0_0F, CurrentIn, CurrentOut, RemainingBytes);
                  ChaCha20AddCounter(state, 1);
                  return;
                }
                T1 = _mm512_loadu_si512((const __m512i *)(CurrentIn + 0 * 64));
                T1 = _mm512_xor_si512(T1, X0_0F);
                _mm512_storeu_si512(CurrentOut + 0 * 64, T1);

                RemainingBytes -= 64;
                if (RemainingBytes == 0)
                {
                  ChaCha20AddCounter(state, 1);
                  return;
                }

                CurrentIn += 64;
                CurrentOut += 64;

                if (RemainingBytes < 64)
                {
                  PartialXor(X0_1F, CurrentIn, CurrentOut, RemainingBytes);
                  ChaCha20AddCounter(state, 2);
                  return;
                }
                T1 = _mm512_loadu_si512((const __m512i *)(CurrentIn));
                T1 = _mm512_xor_si512(T1, X0_1F);
                _mm512_storeu_si512(CurrentOut, T1);

                RemainingBytes -= 64;
                if (RemainingBytes == 0)
                {
                  ChaCha20AddCounter(state, 2);
                  return;
                }

                CurrentIn += 64;
                CurrentOut += 64;

                if (RemainingBytes < 64)
                {
                  PartialXor(X0_2F, CurrentIn, CurrentOut, RemainingBytes);
                  ChaCha20AddCounter(state, 3);
                  return;
                }
                T1 = _mm512_loadu_si512((const __m512i *)(CurrentIn));
                T1 = _mm512_xor_si512(T1, X0_2F);
                _mm512_storeu_si512(CurrentOut, T1);

                RemainingBytes -= 64;
                if (RemainingBytes == 0)
                {
                  ChaCha20AddCounter(state, 3);
                  return;
                }

                PartialXor(X0_3, CurrentIn, CurrentOut, RemainingBytes);
                ChaCha20AddCounter(state, 4);
                return;
              }
              else
              {
                if (RemainingBytes < 64)
                {
                  PartialStore(X0_0F, CurrentOut, RemainingBytes);
                  ChaCha20AddCounter(state, 1);
                  return;
                }
                _mm512_storeu_si512((__m512i *)(CurrentOut), X0_0F);
                RemainingBytes -= 64;
                if (RemainingBytes == 0)
                {
                  ChaCha20AddCounter(state, 1);
                  return;
                }
                CurrentOut += 64;

                if (RemainingBytes < 64)
                {
                  PartialStore(X0_1F, CurrentOut, RemainingBytes);
                  ChaCha20AddCounter(state, 2);
                  return;
                }
                _mm512_storeu_si512((__m512i *)(CurrentOut), X0_1F);
                RemainingBytes -= 64;
                if (RemainingBytes == 0)
                {
                  ChaCha20AddCounter(state, 2);
                  return;
                }
                CurrentOut += 64;

                if (RemainingBytes < 64)
                {
                  PartialStore(X0_2F, CurrentOut, RemainingBytes);
                  ChaCha20AddCounter(state, 3);
                  return;
                }
                _mm512_storeu_si512((__m512i *)(CurrentOut), X0_2F);
                RemainingBytes -= 64;
                if (RemainingBytes == 0)
                {
                  ChaCha20AddCounter(state, 3);
                  return;
                }
                CurrentOut += 64;

                PartialStore(X0_3F, CurrentOut, RemainingBytes);
                ChaCha20AddCounter(state, 4);
                return;
              }
            }
          }
        }

    #elif defined(__AVX2__)

        static inline void PartialXor(const __m256i val, const uint8_t* Src, uint8_t* Dest, uint64_t Size)
        {
                _Alignas(32) uint8_t BuffForPartialOp[32];
                memcpy(BuffForPartialOp, Src, Size);
                _mm256_storeu_si256((__m256i*)(BuffForPartialOp), _mm256_xor_si256(val, _mm256_loadu_si256((const __m256i*)BuffForPartialOp)));
                memcpy(Dest, BuffForPartialOp, Size);
        }

        static inline void PartialStore(const __m256i val, uint8_t* Dest, uint64_t Size)
        {
                _Alignas(32) uint8_t BuffForPartialOp[32];
                _mm256_storeu_si256((__m256i*)(BuffForPartialOp), val);
                memcpy(Dest, BuffForPartialOp, Size);
        }

        static inline __m256i RotateLeft7(const __m256i val) {
                return _mm256_or_si256(_mm256_slli_epi32(val, 7),
                        _mm256_srli_epi32(val, 32 - 7));
        }

        static inline __m256i RotateLeft12(const __m256i val) {
                return _mm256_or_si256(_mm256_slli_epi32(val, 12),
                        _mm256_srli_epi32(val, 32 - 12));
        }

        static inline __m256i RotateLeft8(const __m256i val) {
                const __m256i mask =
                        _mm256_set_epi8(14, 13, 12, 15, 10, 9, 8, 11, 6, 5, 4, 7, 2, 1, 0, 3, 14,
                                13, 12, 15, 10, 9, 8, 11, 6, 5, 4, 7, 2, 1, 0, 3);
                return _mm256_shuffle_epi8(val, mask);
        }

        static inline __m256i RotateLeft16(const __m256i val) {
                const __m256i mask =
                        _mm256_set_epi8(13, 12, 15, 14, 9, 8, 11, 10, 5, 4, 7, 6, 1, 0, 3, 2, 13,
                                12, 15, 14, 9, 8, 11, 10, 5, 4, 7, 6, 1, 0, 3, 2);
                return _mm256_shuffle_epi8(val, mask);
        }

        void ChaCha20EncryptBytes(uint8_t* state, uint8_t* In, uint8_t* Out, uint64_t Size, int rounds)
        {
                uint8_t* CurrentIn = In;
                uint8_t* CurrentOut = Out;

                uint64_t FullBlocksCount = Size / 512;
                uint64_t RemainingBytes = Size % 512;

                const __m256i state0 = _mm256_broadcastsi128_si256(_mm_set_epi32(1797285236, 2036477234, 857760878, 1634760805)); //"expand 32-byte k"
                const __m256i state1 = _mm256_broadcastsi128_si256(_mm_load_si128((const __m128i*)(state)));
                const __m256i state2 = _mm256_broadcastsi128_si256(_mm_load_si128((const __m128i*)(state + 16)));

                __m256i CTR0 = _mm256_set_epi32(0, 0, 0, 0, 0, 0, 0, 4);
                const __m256i CTR1 = _mm256_set_epi32(0, 0, 0, 1, 0, 0, 0, 5);
                const __m256i CTR2 = _mm256_set_epi32(0, 0, 0, 2, 0, 0, 0, 6);
                const __m256i CTR3 = _mm256_set_epi32(0, 0, 0, 3, 0, 0, 0, 7);

                for (int64_t n = 0; n < FullBlocksCount; n++)
                {

                        const __m256i state3 = _mm256_broadcastsi128_si256(
                                _mm_load_si128((const __m128i*)(state + 32)));

                        __m256i X0_0 = state0;
                        __m256i X0_1 = state1;
                        __m256i X0_2 = state2;
                        __m256i X0_3 = _mm256_add_epi32(state3, CTR0);

                        __m256i X1_0 = state0;
                        __m256i X1_1 = state1;
                        __m256i X1_2 = state2;
                        __m256i X1_3 = _mm256_add_epi32(state3, CTR1);

                        __m256i X2_0 = state0;
                        __m256i X2_1 = state1;
                        __m256i X2_2 = state2;
                        __m256i X2_3 = _mm256_add_epi32(state3, CTR2);

                        __m256i X3_0 = state0;
                        __m256i X3_1 = state1;
                        __m256i X3_2 = state2;
                        __m256i X3_3 = _mm256_add_epi32(state3, CTR3);

                        for (int i = rounds; i > 0; i -= 2)
                        {
                                X0_0 = _mm256_add_epi32(X0_0, X0_1);
                                X1_0 = _mm256_add_epi32(X1_0, X1_1);
                                X2_0 = _mm256_add_epi32(X2_0, X2_1);
                                X3_0 = _mm256_add_epi32(X3_0, X3_1);

                                X0_3 = _mm256_xor_si256(X0_3, X0_0);
                                X1_3 = _mm256_xor_si256(X1_3, X1_0);
                                X2_3 = _mm256_xor_si256(X2_3, X2_0);
                                X3_3 = _mm256_xor_si256(X3_3, X3_0);

                                X0_3 = RotateLeft16(X0_3);
                                X1_3 = RotateLeft16(X1_3);
                                X2_3 = RotateLeft16(X2_3);
                                X3_3 = RotateLeft16(X3_3);

                                X0_2 = _mm256_add_epi32(X0_2, X0_3);
                                X1_2 = _mm256_add_epi32(X1_2, X1_3);
                                X2_2 = _mm256_add_epi32(X2_2, X2_3);
                                X3_2 = _mm256_add_epi32(X3_2, X3_3);

                                X0_1 = _mm256_xor_si256(X0_1, X0_2);
                                X1_1 = _mm256_xor_si256(X1_1, X1_2);
                                X2_1 = _mm256_xor_si256(X2_1, X2_2);
                                X3_1 = _mm256_xor_si256(X3_1, X3_2);

                                X0_1 = RotateLeft12(X0_1);
                                X1_1 = RotateLeft12(X1_1);
                                X2_1 = RotateLeft12(X2_1);
                                X3_1 = RotateLeft12(X3_1);

                                X0_0 = _mm256_add_epi32(X0_0, X0_1);
                                X1_0 = _mm256_add_epi32(X1_0, X1_1);
                                X2_0 = _mm256_add_epi32(X2_0, X2_1);
                                X3_0 = _mm256_add_epi32(X3_0, X3_1);

                                X0_3 = _mm256_xor_si256(X0_3, X0_0);
                                X1_3 = _mm256_xor_si256(X1_3, X1_0);
                                X2_3 = _mm256_xor_si256(X2_3, X2_0);
                                X3_3 = _mm256_xor_si256(X3_3, X3_0);

                                X0_3 = RotateLeft8(X0_3);
                                X1_3 = RotateLeft8(X1_3);
                                X2_3 = RotateLeft8(X2_3);
                                X3_3 = RotateLeft8(X3_3);

                                X0_2 = _mm256_add_epi32(X0_2, X0_3);
                                X1_2 = _mm256_add_epi32(X1_2, X1_3);
                                X2_2 = _mm256_add_epi32(X2_2, X2_3);
                                X3_2 = _mm256_add_epi32(X3_2, X3_3);

                                X0_1 = _mm256_xor_si256(X0_1, X0_2);
                                X1_1 = _mm256_xor_si256(X1_1, X1_2);
                                X2_1 = _mm256_xor_si256(X2_1, X2_2);
                                X3_1 = _mm256_xor_si256(X3_1, X3_2);

                                X0_1 = RotateLeft7(X0_1);
                                X1_1 = RotateLeft7(X1_1);
                                X2_1 = RotateLeft7(X2_1);
                                X3_1 = RotateLeft7(X3_1);

                                X0_1 = _mm256_shuffle_epi32(X0_1, _MM_SHUFFLE(0, 3, 2, 1));
                                X0_2 = _mm256_shuffle_epi32(X0_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X0_3 = _mm256_shuffle_epi32(X0_3, _MM_SHUFFLE(2, 1, 0, 3));

                                X1_1 = _mm256_shuffle_epi32(X1_1, _MM_SHUFFLE(0, 3, 2, 1));
                                X1_2 = _mm256_shuffle_epi32(X1_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X1_3 = _mm256_shuffle_epi32(X1_3, _MM_SHUFFLE(2, 1, 0, 3));

                                X2_1 = _mm256_shuffle_epi32(X2_1, _MM_SHUFFLE(0, 3, 2, 1));
                                X2_2 = _mm256_shuffle_epi32(X2_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X2_3 = _mm256_shuffle_epi32(X2_3, _MM_SHUFFLE(2, 1, 0, 3));

                                X3_1 = _mm256_shuffle_epi32(X3_1, _MM_SHUFFLE(0, 3, 2, 1));
                                X3_2 = _mm256_shuffle_epi32(X3_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X3_3 = _mm256_shuffle_epi32(X3_3, _MM_SHUFFLE(2, 1, 0, 3));

                                X0_0 = _mm256_add_epi32(X0_0, X0_1);
                                X1_0 = _mm256_add_epi32(X1_0, X1_1);
                                X2_0 = _mm256_add_epi32(X2_0, X2_1);
                                X3_0 = _mm256_add_epi32(X3_0, X3_1);

                                X0_3 = _mm256_xor_si256(X0_3, X0_0);
                                X1_3 = _mm256_xor_si256(X1_3, X1_0);
                                X2_3 = _mm256_xor_si256(X2_3, X2_0);
                                X3_3 = _mm256_xor_si256(X3_3, X3_0);

                                X0_3 = RotateLeft16(X0_3);
                                X1_3 = RotateLeft16(X1_3);
                                X2_3 = RotateLeft16(X2_3);
                                X3_3 = RotateLeft16(X3_3);

                                X0_2 = _mm256_add_epi32(X0_2, X0_3);
                                X1_2 = _mm256_add_epi32(X1_2, X1_3);
                                X2_2 = _mm256_add_epi32(X2_2, X2_3);
                                X3_2 = _mm256_add_epi32(X3_2, X3_3);

                                X0_1 = _mm256_xor_si256(X0_1, X0_2);
                                X1_1 = _mm256_xor_si256(X1_1, X1_2);
                                X2_1 = _mm256_xor_si256(X2_1, X2_2);
                                X3_1 = _mm256_xor_si256(X3_1, X3_2);

                                X0_1 = RotateLeft12(X0_1);
                                X1_1 = RotateLeft12(X1_1);
                                X2_1 = RotateLeft12(X2_1);
                                X3_1 = RotateLeft12(X3_1);

                                X0_0 = _mm256_add_epi32(X0_0, X0_1);
                                X1_0 = _mm256_add_epi32(X1_0, X1_1);
                                X2_0 = _mm256_add_epi32(X2_0, X2_1);
                                X3_0 = _mm256_add_epi32(X3_0, X3_1);

                                X0_3 = _mm256_xor_si256(X0_3, X0_0);
                                X1_3 = _mm256_xor_si256(X1_3, X1_0);
                                X2_3 = _mm256_xor_si256(X2_3, X2_0);
                                X3_3 = _mm256_xor_si256(X3_3, X3_0);

                                X0_3 = RotateLeft8(X0_3);
                                X1_3 = RotateLeft8(X1_3);
                                X2_3 = RotateLeft8(X2_3);
                                X3_3 = RotateLeft8(X3_3);

                                X0_2 = _mm256_add_epi32(X0_2, X0_3);
                                X1_2 = _mm256_add_epi32(X1_2, X1_3);
                                X2_2 = _mm256_add_epi32(X2_2, X2_3);
                                X3_2 = _mm256_add_epi32(X3_2, X3_3);

                                X0_1 = _mm256_xor_si256(X0_1, X0_2);
                                X1_1 = _mm256_xor_si256(X1_1, X1_2);
                                X2_1 = _mm256_xor_si256(X2_1, X2_2);
                                X3_1 = _mm256_xor_si256(X3_1, X3_2);

                                X0_1 = RotateLeft7(X0_1);
                                X1_1 = RotateLeft7(X1_1);
                                X2_1 = RotateLeft7(X2_1);
                                X3_1 = RotateLeft7(X3_1);

                                X0_1 = _mm256_shuffle_epi32(X0_1, _MM_SHUFFLE(2, 1, 0, 3));
                                X0_2 = _mm256_shuffle_epi32(X0_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X0_3 = _mm256_shuffle_epi32(X0_3, _MM_SHUFFLE(0, 3, 2, 1));

                                X1_1 = _mm256_shuffle_epi32(X1_1, _MM_SHUFFLE(2, 1, 0, 3));
                                X1_2 = _mm256_shuffle_epi32(X1_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X1_3 = _mm256_shuffle_epi32(X1_3, _MM_SHUFFLE(0, 3, 2, 1));

                                X2_1 = _mm256_shuffle_epi32(X2_1, _MM_SHUFFLE(2, 1, 0, 3));
                                X2_2 = _mm256_shuffle_epi32(X2_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X2_3 = _mm256_shuffle_epi32(X2_3, _MM_SHUFFLE(0, 3, 2, 1));

                                X3_1 = _mm256_shuffle_epi32(X3_1, _MM_SHUFFLE(2, 1, 0, 3));
                                X3_2 = _mm256_shuffle_epi32(X3_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X3_3 = _mm256_shuffle_epi32(X3_3, _MM_SHUFFLE(0, 3, 2, 1));
                        }

                        X0_0 = _mm256_add_epi32(X0_0, state0);
                        X0_1 = _mm256_add_epi32(X0_1, state1);
                        X0_2 = _mm256_add_epi32(X0_2, state2);
                        X0_3 = _mm256_add_epi32(X0_3, state3);
                        X0_3 = _mm256_add_epi32(X0_3, CTR0);

                        X1_0 = _mm256_add_epi32(X1_0, state0);
                        X1_1 = _mm256_add_epi32(X1_1, state1);
                        X1_2 = _mm256_add_epi32(X1_2, state2);
                        X1_3 = _mm256_add_epi32(X1_3, state3);
                        X1_3 = _mm256_add_epi32(X1_3, CTR1);

                        X2_0 = _mm256_add_epi32(X2_0, state0);
                        X2_1 = _mm256_add_epi32(X2_1, state1);
                        X2_2 = _mm256_add_epi32(X2_2, state2);
                        X2_3 = _mm256_add_epi32(X2_3, state3);
                        X2_3 = _mm256_add_epi32(X2_3, CTR2);

                        X3_0 = _mm256_add_epi32(X3_0, state0);
                        X3_1 = _mm256_add_epi32(X3_1, state1);
                        X3_2 = _mm256_add_epi32(X3_2, state2);
                        X3_3 = _mm256_add_epi32(X3_3, state3);
                        X3_3 = _mm256_add_epi32(X3_3, CTR3);

                        //


                        if (In)
                        {
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 0 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X0_0, X0_1, 1 + (3 << 4)),
                                                _mm256_loadu_si256((__m256i*)(CurrentIn + 0 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 1 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X0_2, X0_3, 1 + (3 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 1 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 2 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X1_0, X1_1, 1 + (3 << 4)),
                                                _mm256_loadu_si256(((const __m256i*)(CurrentIn + 2 * 32)))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 3 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X1_2, X1_3, 1 + (3 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 3 * 32))));

                                _mm256_storeu_si256((__m256i*)(CurrentOut + 4 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X2_0, X2_1, 1 + (3 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 4 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 5 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X2_2, X2_3, 1 + (3 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 5 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 6 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X3_0, X3_1, 1 + (3 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 6 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 7 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X3_2, X3_3, 1 + (3 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 7 * 32))));

                                _mm256_storeu_si256(
                                        (__m256i*)(CurrentOut + 8 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X0_0, X0_1, 0 + (2 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 8 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 9 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X0_2, X0_3, 0 + (2 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 9 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 10 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X1_0, X1_1, 0 + (2 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 10 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 11 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X1_2, X1_3, 0 + (2 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 11 * 32))));

                                _mm256_storeu_si256((__m256i*)(CurrentOut + 12 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X2_0, X2_1, 0 + (2 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 12 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 13 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X2_2, X2_3, 0 + (2 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 13 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 14 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X3_0, X3_1, 0 + (2 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 14 * 32))));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 15 * 32),
                                        _mm256_xor_si256(_mm256_permute2x128_si256(X3_2, X3_3, 0 + (2 << 4)),
                                                _mm256_loadu_si256((const __m256i*)(CurrentIn + 15 * 32))));
                        }
                        else
                        {
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 0 * 32),
                                        _mm256_permute2x128_si256(X0_0, X0_1, 1 + (3 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 1 * 32),
                                        _mm256_permute2x128_si256(X0_2, X0_3, 1 + (3 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 2 * 32),
                                        _mm256_permute2x128_si256(X1_0, X1_1, 1 + (3 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 3 * 32),
                                        _mm256_permute2x128_si256(X1_2, X1_3, 1 + (3 << 4)));

                                _mm256_storeu_si256((__m256i*)(CurrentOut + 4 * 32),
                                        _mm256_permute2x128_si256(X2_0, X2_1, 1 + (3 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 5 * 32),
                                        _mm256_permute2x128_si256(X2_2, X2_3, 1 + (3 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 6 * 32),
                                        _mm256_permute2x128_si256(X3_0, X3_1, 1 + (3 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 7 * 32),
                                        _mm256_permute2x128_si256(X3_2, X3_3, 1 + (3 << 4)));

                                _mm256_storeu_si256((__m256i*)(CurrentOut + 8 * 32),
                                        _mm256_permute2x128_si256(X0_0, X0_1, 0 + (2 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 9 * 32),
                                        _mm256_permute2x128_si256(X0_2, X0_3, 0 + (2 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 10 * 32),
                                        _mm256_permute2x128_si256(X1_0, X1_1, 0 + (2 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 11 * 32),
                                        _mm256_permute2x128_si256(X1_2, X1_3, 0 + (2 << 4)));

                                _mm256_storeu_si256((__m256i*)(CurrentOut + 12 * 32),
                                        _mm256_permute2x128_si256(X2_0, X2_1, 0 + (2 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 13 * 32),
                                        _mm256_permute2x128_si256(X2_2, X2_3, 0 + (2 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 14 * 32),
                                        _mm256_permute2x128_si256(X3_0, X3_1, 0 + (2 << 4)));
                                _mm256_storeu_si256((__m256i*)(CurrentOut + 15 * 32),
                                        _mm256_permute2x128_si256(X3_2, X3_3, 0 + (2 << 4)));
                        }

                        ChaCha20AddCounter(state, 8);
                        if (CurrentIn)
                                CurrentIn += 512;
                        CurrentOut += 512;

                }

                if (RemainingBytes == 0) return;

                CTR0 = _mm256_set_epi32(0, 0, 0, 0, 0, 0, 0, 1);

                while (1)
                {

                        const __m256i state3 = _mm256_broadcastsi128_si256(
                                _mm_load_si128((const __m128i*)(state + 32)));

                        __m256i X0_0 = state0;
                        __m256i X0_1 = state1;
                        __m256i X0_2 = state2;
                        __m256i X0_3 = _mm256_add_epi32(state3, CTR0);


                        for (int i = rounds; i > 0; i -= 2)
                        {
                                X0_0 = _mm256_add_epi32(X0_0, X0_1);


                                X0_3 = _mm256_xor_si256(X0_3, X0_0);

                                X0_3 = RotateLeft16(X0_3);

                                X0_2 = _mm256_add_epi32(X0_2, X0_3);


                                X0_1 = _mm256_xor_si256(X0_1, X0_2);

                                X0_1 = RotateLeft12(X0_1);

                                X0_0 = _mm256_add_epi32(X0_0, X0_1);

                                X0_3 = _mm256_xor_si256(X0_3, X0_0);

                                X0_3 = RotateLeft8(X0_3);

                                X0_2 = _mm256_add_epi32(X0_2, X0_3);


                                X0_1 = _mm256_xor_si256(X0_1, X0_2);

                                X0_1 = RotateLeft7(X0_1);

                                X0_1 = _mm256_shuffle_epi32(X0_1, _MM_SHUFFLE(0, 3, 2, 1));
                                X0_2 = _mm256_shuffle_epi32(X0_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X0_3 = _mm256_shuffle_epi32(X0_3, _MM_SHUFFLE(2, 1, 0, 3));


                                X0_0 = _mm256_add_epi32(X0_0, X0_1);

                                X0_3 = _mm256_xor_si256(X0_3, X0_0);

                                X0_3 = RotateLeft16(X0_3);

                                X0_2 = _mm256_add_epi32(X0_2, X0_3);

                                X0_1 = _mm256_xor_si256(X0_1, X0_2);

                                X0_1 = RotateLeft12(X0_1);

                                X0_0 = _mm256_add_epi32(X0_0, X0_1);

                                X0_3 = _mm256_xor_si256(X0_3, X0_0);

                                X0_3 = RotateLeft8(X0_3);

                                X0_2 = _mm256_add_epi32(X0_2, X0_3);

                                X0_1 = _mm256_xor_si256(X0_1, X0_2);

                                X0_1 = RotateLeft7(X0_1);

                                X0_1 = _mm256_shuffle_epi32(X0_1, _MM_SHUFFLE(2, 1, 0, 3));
                                X0_2 = _mm256_shuffle_epi32(X0_2, _MM_SHUFFLE(1, 0, 3, 2));
                                X0_3 = _mm256_shuffle_epi32(X0_3, _MM_SHUFFLE(0, 3, 2, 1));


                        }

                        X0_0 = _mm256_add_epi32(X0_0, state0);
                        X0_1 = _mm256_add_epi32(X0_1, state1);
                        X0_2 = _mm256_add_epi32(X0_2, state2);
                        X0_3 = _mm256_add_epi32(X0_3, state3);
                        X0_3 = _mm256_add_epi32(X0_3, CTR0);

                        //todo

                        if (RemainingBytes >= 128)
                        {
                                if (In)
                                {
                                        _mm256_storeu_si256((__m256i*)(CurrentOut + 0 * 32),
                                                _mm256_xor_si256(_mm256_permute2x128_si256(X0_0, X0_1, 1 + (3 << 4)),
                                                        _mm256_loadu_si256((__m256i*)(CurrentIn + 0 * 32))));
                                        _mm256_storeu_si256((__m256i*)(CurrentOut + 1 * 32),
                                                _mm256_xor_si256(_mm256_permute2x128_si256(X0_2, X0_3, 1 + (3 << 4)),
                                                        _mm256_loadu_si256((const __m256i*)(CurrentIn + 1 * 32))));
                                        _mm256_storeu_si256((__m256i*)(CurrentOut + 2 * 32),
                                                _mm256_xor_si256(_mm256_permute2x128_si256(X0_0, X0_1, 0 + (2 << 4)),
                                                        _mm256_loadu_si256((const __m256i*)(CurrentIn + 2 * 32))));
                                        _mm256_storeu_si256((__m256i*)(CurrentOut + 3 * 32),
                                                _mm256_xor_si256(_mm256_permute2x128_si256(X0_2, X0_3, 0 + (2 << 4)),
                                                        _mm256_loadu_si256((const __m256i*)(CurrentIn + 3 * 32))));

                                }
                                else
                                {
                                        _mm256_storeu_si256((__m256i*)(CurrentOut + 0 * 32),
                                                _mm256_permute2x128_si256(X0_0, X0_1, 1 + (3 << 4)));
                                        _mm256_storeu_si256((__m256i*)(CurrentOut + 1 * 32),
                                                _mm256_permute2x128_si256(X0_2, X0_3, 1 + (3 << 4)));
                                        _mm256_storeu_si256((__m256i*)(CurrentOut + 2 * 32),
                                                _mm256_permute2x128_si256(X0_0, X0_1, 0 + (2 << 4)));
                                        _mm256_storeu_si256((__m256i*)(CurrentOut + 3 * 32),
                                                _mm256_permute2x128_si256(X0_2, X0_3, 0 + (2 << 4)));

                                }
                                ChaCha20AddCounter(state, 2);
                                RemainingBytes -= 128;
                                if (RemainingBytes == 0) return;
                                if (CurrentIn)
                                        CurrentIn += 128;
                                CurrentOut += 128;
                                continue;
                        }
                        else //last, partial block
                        {
                                __m256i tmp;
                                if (In) // encrypt
                                {
                                        tmp = _mm256_permute2x128_si256(X0_0, X0_1, 1 + (3 << 4));
                                        if (RemainingBytes < 32)
                                        {
                                                PartialXor(tmp, CurrentIn, CurrentOut, RemainingBytes);
                                                ChaCha20AddCounter(state, 1);
                                                return;
                                        }
                                        _mm256_storeu_si256((__m256i*)(CurrentOut), _mm256_xor_si256(tmp, _mm256_loadu_si256((const __m256i*)(CurrentIn))));
                                        RemainingBytes -= 32;
                                        if (RemainingBytes == 0)
                                        {
                                                ChaCha20AddCounter(state, 1);
                                                return;
                                        }

                                        CurrentIn += 32;
                                        CurrentOut += 32;



                                        tmp = _mm256_permute2x128_si256(X0_2, X0_3, 1 + (3 << 4));
                                        if (RemainingBytes < 32)
                                        {
                                                PartialXor(tmp, CurrentIn, CurrentOut, RemainingBytes);
                                                ChaCha20AddCounter(state, 1);
                                                return;
                                        }
                                        _mm256_storeu_si256((__m256i*)(CurrentOut), _mm256_xor_si256(tmp, _mm256_loadu_si256((const __m256i*)(CurrentIn))));
                                        RemainingBytes -= 32;
                                        if (RemainingBytes == 0)
                                        {
                                                ChaCha20AddCounter(state, 1);
                                                return;
                                        }
                                        CurrentIn += 32;
                                        CurrentOut += 32;


                                        tmp = _mm256_permute2x128_si256(X0_0, X0_1, 0 + (2 << 4));
                                        if (RemainingBytes < 32)
                                        {
                                                PartialXor(tmp, CurrentIn, CurrentOut, RemainingBytes);
                                                ChaCha20AddCounter(state, 2);
                                                return;
                                        }
                                        _mm256_storeu_si256((__m256i*)(CurrentOut), _mm256_xor_si256(tmp, _mm256_loadu_si256((const __m256i*)(CurrentIn))));
                                        RemainingBytes -= 32;
                                        if (RemainingBytes == 0)
                                        {
                                                ChaCha20AddCounter(state, 2);
                                                return;
                                        }
                                        CurrentIn += 32;
                                        CurrentOut += 32;


                                        tmp = _mm256_permute2x128_si256(X0_2, X0_3, 0 + (2 << 4));
                                        PartialXor(tmp, CurrentIn, CurrentOut, RemainingBytes);
                                        ChaCha20AddCounter(state, 2);
                                        return;
                                }
                                else
                                {

                                        tmp = _mm256_permute2x128_si256(X0_0, X0_1, 1 + (3 << 4));
                                        if (RemainingBytes < 32)
                                        {
                                                PartialStore(tmp, CurrentOut, RemainingBytes);
                                                ChaCha20AddCounter(state, 1);
                                                return;
                                        }
                                        _mm256_storeu_si256((__m256i*)(CurrentOut), tmp);
                                        RemainingBytes -= 32;
                                        if (RemainingBytes == 0)
                                        {
                                                ChaCha20AddCounter(state, 1);
                                                return;
                                        }
                                        CurrentOut += 32;


                                        tmp = _mm256_permute2x128_si256(X0_2, X0_3, 1 + (3 << 4));

                                        if (RemainingBytes < 32)
                                        {
                                                PartialStore(tmp, CurrentOut, RemainingBytes);
                                                ChaCha20AddCounter(state, 1);
                                                return;
                                        }
                                        _mm256_storeu_si256((__m256i*)(CurrentOut), tmp);
                                        RemainingBytes -= 32;
                                        if (RemainingBytes == 0)
                                        {
                                                ChaCha20AddCounter(state, 1);
                                                return;
                                        }
                                        CurrentOut += 32;


                                        tmp = _mm256_permute2x128_si256(X0_0, X0_1, 0 + (2 << 4));
                                        if (RemainingBytes < 32)
                                        {
                                                PartialStore(tmp, CurrentOut, RemainingBytes);
                                                ChaCha20AddCounter(state, 2);
                                                return;
                                        }
                                        _mm256_storeu_si256((__m256i*)(CurrentOut), tmp);
                                        RemainingBytes -= 32;
                                        if (RemainingBytes == 0)
                                        {
                                                ChaCha20AddCounter(state, 2);
                                                return;
                                        }
                                        CurrentOut += 32;


                                        tmp = _mm256_permute2x128_si256(X0_2, X0_3, 0 + (2 << 4));
                                        PartialStore(tmp, CurrentOut, RemainingBytes);
                                        ChaCha20AddCounter(state, 2);
                                        return;
                                }
                        }
                }
        }

    #elif defined(__SSE2__)

        static inline void PartialXor(const __m128i val, uint8_t* Src, uint8_t* Dest, uint64_t Size)
        {
                _Alignas(16) uint8_t BuffForPartialOp[16];
                memcpy(BuffForPartialOp, Src, Size);
                _mm_storeu_si128((__m128i*)(BuffForPartialOp), _mm_xor_si128(val, _mm_loadu_si128((const __m128i*)BuffForPartialOp)));
                memcpy(Dest, BuffForPartialOp, Size);
        }

        static inline void PartialStore(const __m128i val, uint8_t* Dest, uint64_t Size)
        {
                _Alignas(16) uint8_t BuffForPartialOp[16];
                _mm_storeu_si128((__m128i*)(BuffForPartialOp), val);
                memcpy(Dest, BuffForPartialOp, Size);
        }

        static inline __m128i RotateLeft7(const __m128i val)
        {
                return _mm_or_si128(_mm_slli_epi32(val, 7), _mm_srli_epi32(val, 32 - 7));
        }

        static inline __m128i RotateLeft8(const __m128i val)
        {
                return _mm_or_si128(_mm_slli_epi32(val, 8), _mm_srli_epi32(val, 32 - 8));
        }

        static inline __m128i RotateLeft12(const __m128i val)
        {
                return _mm_or_si128(_mm_slli_epi32(val, 12), _mm_srli_epi32(val, 32 - 12));
        }

        static inline __m128i RotateLeft16(const __m128i val)
        {
                return _mm_or_si128(_mm_slli_epi32(val, 16), _mm_srli_epi32(val, 32 - 16));
        }

        void ChaCha20EncryptBytes(uint8_t* state, uint8_t* In, uint8_t* Out, uint64_t Size, int rounds)
        {

                uint8_t* CurrentIn = In;
                uint8_t* CurrentOut = Out;


                uint64_t FullBlocksCount = Size / 256;
                uint64_t RemainingBytes = Size % 256;

                const __m128i state0 = _mm_set_epi32(1797285236, 2036477234, 857760878, 1634760805); //"expand 32-byte k"
                const __m128i state1 = _mm_loadu_si128((const __m128i*)(state));
                const __m128i state2 = _mm_loadu_si128((const __m128i*)((state)+16));

                for (int64_t n = 0; n < FullBlocksCount; n++)
                {

                        const __m128i state3 = _mm_loadu_si128((const __m128i*)((state)+32));

                        __m128i r0_0 = state0;
                        __m128i r0_1 = state1;
                        __m128i r0_2 = state2;
                        __m128i r0_3 = state3;

                        __m128i r1_0 = state0;
                        __m128i r1_1 = state1;
                        __m128i r1_2 = state2;
                        __m128i r1_3 = _mm_add_epi64(r0_3, _mm_set_epi32(0, 0, 0, 1));

                        __m128i r2_0 = state0;
                        __m128i r2_1 = state1;
                        __m128i r2_2 = state2;
                        __m128i r2_3 = _mm_add_epi64(r0_3, _mm_set_epi32(0, 0, 0, 2));

                        __m128i r3_0 = state0;
                        __m128i r3_1 = state1;
                        __m128i r3_2 = state2;
                        __m128i r3_3 = _mm_add_epi64(r0_3, _mm_set_epi32(0, 0, 0, 3));

                        for (int i = rounds; i > 0; i -= 2)
                        {
                                r0_0 = _mm_add_epi32(r0_0, r0_1);
                                r1_0 = _mm_add_epi32(r1_0, r1_1);
                                r2_0 = _mm_add_epi32(r2_0, r2_1);
                                r3_0 = _mm_add_epi32(r3_0, r3_1);

                                r0_3 = _mm_xor_si128(r0_3, r0_0);
                                r1_3 = _mm_xor_si128(r1_3, r1_0);
                                r2_3 = _mm_xor_si128(r2_3, r2_0);
                                r3_3 = _mm_xor_si128(r3_3, r3_0);

                                r0_3 = RotateLeft16(r0_3);
                                r1_3 = RotateLeft16(r1_3);
                                r2_3 = RotateLeft16(r2_3);
                                r3_3 = RotateLeft16(r3_3);

                                r0_2 = _mm_add_epi32(r0_2, r0_3);
                                r1_2 = _mm_add_epi32(r1_2, r1_3);
                                r2_2 = _mm_add_epi32(r2_2, r2_3);
                                r3_2 = _mm_add_epi32(r3_2, r3_3);

                                r0_1 = _mm_xor_si128(r0_1, r0_2);
                                r1_1 = _mm_xor_si128(r1_1, r1_2);
                                r2_1 = _mm_xor_si128(r2_1, r2_2);
                                r3_1 = _mm_xor_si128(r3_1, r3_2);

                                r0_1 = RotateLeft12(r0_1);
                                r1_1 = RotateLeft12(r1_1);
                                r2_1 = RotateLeft12(r2_1);
                                r3_1 = RotateLeft12(r3_1);

                                r0_0 = _mm_add_epi32(r0_0, r0_1);
                                r1_0 = _mm_add_epi32(r1_0, r1_1);
                                r2_0 = _mm_add_epi32(r2_0, r2_1);
                                r3_0 = _mm_add_epi32(r3_0, r3_1);

                                r0_3 = _mm_xor_si128(r0_3, r0_0);
                                r1_3 = _mm_xor_si128(r1_3, r1_0);
                                r2_3 = _mm_xor_si128(r2_3, r2_0);
                                r3_3 = _mm_xor_si128(r3_3, r3_0);

                                r0_3 = RotateLeft8(r0_3);
                                r1_3 = RotateLeft8(r1_3);
                                r2_3 = RotateLeft8(r2_3);
                                r3_3 = RotateLeft8(r3_3);

                                r0_2 = _mm_add_epi32(r0_2, r0_3);
                                r1_2 = _mm_add_epi32(r1_2, r1_3);
                                r2_2 = _mm_add_epi32(r2_2, r2_3);
                                r3_2 = _mm_add_epi32(r3_2, r3_3);

                                r0_1 = _mm_xor_si128(r0_1, r0_2);
                                r1_1 = _mm_xor_si128(r1_1, r1_2);
                                r2_1 = _mm_xor_si128(r2_1, r2_2);
                                r3_1 = _mm_xor_si128(r3_1, r3_2);

                                r0_1 = RotateLeft7(r0_1);
                                r1_1 = RotateLeft7(r1_1);
                                r2_1 = RotateLeft7(r2_1);
                                r3_1 = RotateLeft7(r3_1);

                                r0_1 = _mm_shuffle_epi32(r0_1, _MM_SHUFFLE(0, 3, 2, 1));
                                r0_2 = _mm_shuffle_epi32(r0_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r0_3 = _mm_shuffle_epi32(r0_3, _MM_SHUFFLE(2, 1, 0, 3));

                                r1_1 = _mm_shuffle_epi32(r1_1, _MM_SHUFFLE(0, 3, 2, 1));
                                r1_2 = _mm_shuffle_epi32(r1_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r1_3 = _mm_shuffle_epi32(r1_3, _MM_SHUFFLE(2, 1, 0, 3));

                                r2_1 = _mm_shuffle_epi32(r2_1, _MM_SHUFFLE(0, 3, 2, 1));
                                r2_2 = _mm_shuffle_epi32(r2_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r2_3 = _mm_shuffle_epi32(r2_3, _MM_SHUFFLE(2, 1, 0, 3));

                                r3_1 = _mm_shuffle_epi32(r3_1, _MM_SHUFFLE(0, 3, 2, 1));
                                r3_2 = _mm_shuffle_epi32(r3_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r3_3 = _mm_shuffle_epi32(r3_3, _MM_SHUFFLE(2, 1, 0, 3));

                                r0_0 = _mm_add_epi32(r0_0, r0_1);
                                r1_0 = _mm_add_epi32(r1_0, r1_1);
                                r2_0 = _mm_add_epi32(r2_0, r2_1);
                                r3_0 = _mm_add_epi32(r3_0, r3_1);

                                r0_3 = _mm_xor_si128(r0_3, r0_0);
                                r1_3 = _mm_xor_si128(r1_3, r1_0);
                                r2_3 = _mm_xor_si128(r2_3, r2_0);
                                r3_3 = _mm_xor_si128(r3_3, r3_0);

                                r0_3 = RotateLeft16(r0_3);
                                r1_3 = RotateLeft16(r1_3);
                                r2_3 = RotateLeft16(r2_3);
                                r3_3 = RotateLeft16(r3_3);

                                r0_2 = _mm_add_epi32(r0_2, r0_3);
                                r1_2 = _mm_add_epi32(r1_2, r1_3);
                                r2_2 = _mm_add_epi32(r2_2, r2_3);
                                r3_2 = _mm_add_epi32(r3_2, r3_3);

                                r0_1 = _mm_xor_si128(r0_1, r0_2);
                                r1_1 = _mm_xor_si128(r1_1, r1_2);
                                r2_1 = _mm_xor_si128(r2_1, r2_2);
                                r3_1 = _mm_xor_si128(r3_1, r3_2);

                                r0_1 = RotateLeft12(r0_1);
                                r1_1 = RotateLeft12(r1_1);
                                r2_1 = RotateLeft12(r2_1);
                                r3_1 = RotateLeft12(r3_1);

                                r0_0 = _mm_add_epi32(r0_0, r0_1);
                                r1_0 = _mm_add_epi32(r1_0, r1_1);
                                r2_0 = _mm_add_epi32(r2_0, r2_1);
                                r3_0 = _mm_add_epi32(r3_0, r3_1);

                                r0_3 = _mm_xor_si128(r0_3, r0_0);
                                r1_3 = _mm_xor_si128(r1_3, r1_0);
                                r2_3 = _mm_xor_si128(r2_3, r2_0);
                                r3_3 = _mm_xor_si128(r3_3, r3_0);

                                r0_3 = RotateLeft8(r0_3);
                                r1_3 = RotateLeft8(r1_3);
                                r2_3 = RotateLeft8(r2_3);
                                r3_3 = RotateLeft8(r3_3);

                                r0_2 = _mm_add_epi32(r0_2, r0_3);
                                r1_2 = _mm_add_epi32(r1_2, r1_3);
                                r2_2 = _mm_add_epi32(r2_2, r2_3);
                                r3_2 = _mm_add_epi32(r3_2, r3_3);

                                r0_1 = _mm_xor_si128(r0_1, r0_2);
                                r1_1 = _mm_xor_si128(r1_1, r1_2);
                                r2_1 = _mm_xor_si128(r2_1, r2_2);
                                r3_1 = _mm_xor_si128(r3_1, r3_2);

                                r0_1 = RotateLeft7(r0_1);
                                r1_1 = RotateLeft7(r1_1);
                                r2_1 = RotateLeft7(r2_1);
                                r3_1 = RotateLeft7(r3_1);

                                r0_1 = _mm_shuffle_epi32(r0_1, _MM_SHUFFLE(2, 1, 0, 3));
                                r0_2 = _mm_shuffle_epi32(r0_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r0_3 = _mm_shuffle_epi32(r0_3, _MM_SHUFFLE(0, 3, 2, 1));

                                r1_1 = _mm_shuffle_epi32(r1_1, _MM_SHUFFLE(2, 1, 0, 3));
                                r1_2 = _mm_shuffle_epi32(r1_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r1_3 = _mm_shuffle_epi32(r1_3, _MM_SHUFFLE(0, 3, 2, 1));

                                r2_1 = _mm_shuffle_epi32(r2_1, _MM_SHUFFLE(2, 1, 0, 3));
                                r2_2 = _mm_shuffle_epi32(r2_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r2_3 = _mm_shuffle_epi32(r2_3, _MM_SHUFFLE(0, 3, 2, 1));

                                r3_1 = _mm_shuffle_epi32(r3_1, _MM_SHUFFLE(2, 1, 0, 3));
                                r3_2 = _mm_shuffle_epi32(r3_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r3_3 = _mm_shuffle_epi32(r3_3, _MM_SHUFFLE(0, 3, 2, 1));
                        }

                        r0_0 = _mm_add_epi32(r0_0, state0);
                        r0_1 = _mm_add_epi32(r0_1, state1);
                        r0_2 = _mm_add_epi32(r0_2, state2);
                        r0_3 = _mm_add_epi32(r0_3, state3);

                        r1_0 = _mm_add_epi32(r1_0, state0);
                        r1_1 = _mm_add_epi32(r1_1, state1);
                        r1_2 = _mm_add_epi32(r1_2, state2);
                        r1_3 = _mm_add_epi32(r1_3, state3);
                        r1_3 = _mm_add_epi64(r1_3, _mm_set_epi32(0, 0, 0, 1));

                        r2_0 = _mm_add_epi32(r2_0, state0);
                        r2_1 = _mm_add_epi32(r2_1, state1);
                        r2_2 = _mm_add_epi32(r2_2, state2);
                        r2_3 = _mm_add_epi32(r2_3, state3);
                        r2_3 = _mm_add_epi64(r2_3, _mm_set_epi32(0, 0, 0, 2));

                        r3_0 = _mm_add_epi32(r3_0, state0);
                        r3_1 = _mm_add_epi32(r3_1, state1);
                        r3_2 = _mm_add_epi32(r3_2, state2);
                        r3_3 = _mm_add_epi32(r3_3, state3);
                        r3_3 = _mm_add_epi64(r3_3, _mm_set_epi32(0, 0, 0, 3));


                        if (In)
                        {
                                _mm_storeu_si128((__m128i*)(CurrentOut + 0 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 0 * 16)), r0_0));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 1 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 1 * 16)), r0_1));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 2 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 2 * 16)), r0_2));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 3 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 3 * 16)), r0_3));

                                _mm_storeu_si128((__m128i*)(CurrentOut + 4 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 4 * 16)), r1_0));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 5 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 5 * 16)), r1_1));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 6 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 6 * 16)), r1_2));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 7 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 7 * 16)), r1_3));

                                _mm_storeu_si128((__m128i*)(CurrentOut + 8 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 8 * 16)), r2_0));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 9 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 9 * 16)), r2_1));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 10 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 10 * 16)), r2_2));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 11 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 11 * 16)), r2_3));

                                _mm_storeu_si128((__m128i*)(CurrentOut + 12 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 12 * 16)), r3_0));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 13 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 13 * 16)), r3_1));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 14 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 14 * 16)), r3_2));
                                _mm_storeu_si128((__m128i*)(CurrentOut + 15 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 15 * 16)), r3_3));
                                CurrentIn += 256;
                        }
                        else
                        {
                                _mm_storeu_si128((__m128i*)(CurrentOut + 0 * 16), r0_0);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 1 * 16), r0_1);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 2 * 16), r0_2);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 3 * 16), r0_3);

                                _mm_storeu_si128((__m128i*)(CurrentOut + 4 * 16), r1_0);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 5 * 16), r1_1);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 6 * 16), r1_2);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 7 * 16), r1_3);

                                _mm_storeu_si128((__m128i*)(CurrentOut + 8 * 16), r2_0);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 9 * 16), r2_1);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 10 * 16), r2_2);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 11 * 16), r2_3);

                                _mm_storeu_si128((__m128i*)(CurrentOut + 12 * 16), r3_0);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 13 * 16), r3_1);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 14 * 16), r3_2);
                                _mm_storeu_si128((__m128i*)(CurrentOut + 15 * 16), r3_3);
                        }

                        CurrentOut += 256;

                        ChaCha20AddCounter(state, 4);

                }

                if (RemainingBytes == 0) return;


                while(1)
                {
                        const __m128i state3 = _mm_loadu_si128((const __m128i*)((state)+32));

                        __m128i r0_0 = state0;
                        __m128i r0_1 = state1;
                        __m128i r0_2 = state2;
                        __m128i r0_3 = state3;



                        for (int i = rounds; i > 0; i -= 2)
                        {
                                r0_0 = _mm_add_epi32(r0_0, r0_1);

                                r0_3 = _mm_xor_si128(r0_3, r0_0);

                                r0_3 = RotateLeft16(r0_3);

                                r0_2 = _mm_add_epi32(r0_2, r0_3);

                                r0_1 = _mm_xor_si128(r0_1, r0_2);

                                r0_1 = RotateLeft12(r0_1);

                                r0_0 = _mm_add_epi32(r0_0, r0_1);

                                r0_3 = _mm_xor_si128(r0_3, r0_0);

                                r0_3 = RotateLeft8(r0_3);

                                r0_2 = _mm_add_epi32(r0_2, r0_3);

                                r0_1 = _mm_xor_si128(r0_1, r0_2);

                                r0_1 = RotateLeft7(r0_1);

                                r0_1 = _mm_shuffle_epi32(r0_1, _MM_SHUFFLE(0, 3, 2, 1));
                                r0_2 = _mm_shuffle_epi32(r0_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r0_3 = _mm_shuffle_epi32(r0_3, _MM_SHUFFLE(2, 1, 0, 3));


                                r0_0 = _mm_add_epi32(r0_0, r0_1);

                                r0_3 = _mm_xor_si128(r0_3, r0_0);

                                r0_3 = RotateLeft16(r0_3);

                                r0_2 = _mm_add_epi32(r0_2, r0_3);

                                r0_1 = _mm_xor_si128(r0_1, r0_2);

                                r0_1 = RotateLeft12(r0_1);

                                r0_0 = _mm_add_epi32(r0_0, r0_1);

                                r0_3 = _mm_xor_si128(r0_3, r0_0);

                                r0_3 = RotateLeft8(r0_3);

                                r0_2 = _mm_add_epi32(r0_2, r0_3);

                                r0_1 = _mm_xor_si128(r0_1, r0_2);

                                r0_1 = RotateLeft7(r0_1);

                                r0_1 = _mm_shuffle_epi32(r0_1, _MM_SHUFFLE(2, 1, 0, 3));
                                r0_2 = _mm_shuffle_epi32(r0_2, _MM_SHUFFLE(1, 0, 3, 2));
                                r0_3 = _mm_shuffle_epi32(r0_3, _MM_SHUFFLE(0, 3, 2, 1));

                        }

                        r0_0 = _mm_add_epi32(r0_0, state0);
                        r0_1 = _mm_add_epi32(r0_1, state1);
                        r0_2 = _mm_add_epi32(r0_2, state2);
                        r0_3 = _mm_add_epi32(r0_3, state3);

                        if (RemainingBytes>=64)
                        {

                                if (In)
                                {
                                        _mm_storeu_si128((__m128i*)(CurrentOut + 0 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 0 * 16)), r0_0));
                                        _mm_storeu_si128((__m128i*)(CurrentOut + 1 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 1 * 16)), r0_1));
                                        _mm_storeu_si128((__m128i*)(CurrentOut + 2 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 2 * 16)), r0_2));
                                        _mm_storeu_si128((__m128i*)(CurrentOut + 3 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(CurrentIn + 3 * 16)), r0_3));
                                        CurrentIn += 64;
                                }
                                else
                                {
                                        _mm_storeu_si128((__m128i*)(CurrentOut + 0 * 16), r0_0);
                                        _mm_storeu_si128((__m128i*)(CurrentOut + 1 * 16), r0_1);
                                        _mm_storeu_si128((__m128i*)(CurrentOut + 2 * 16), r0_2);
                                        _mm_storeu_si128((__m128i*)(CurrentOut + 3 * 16), r0_3);

                                }
                                CurrentOut += 64;
                                ChaCha20AddCounter(state, 1);
                                RemainingBytes -= 64;
                                if (RemainingBytes == 0) return;
                                continue;
                        }
                        else
                        {
                                _Alignas(16) uint8_t TmpBuf[64];
                                if (In)
                                {
                                        memcpy(TmpBuf, CurrentIn, RemainingBytes);
                                        _mm_storeu_si128((__m128i*)(TmpBuf + 0 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(TmpBuf + 0 * 16)), r0_0));
                                        _mm_storeu_si128((__m128i*)(TmpBuf + 1 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(TmpBuf + 1 * 16)), r0_1));
                                        _mm_storeu_si128((__m128i*)(TmpBuf + 2 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(TmpBuf + 2 * 16)), r0_2));
                                        _mm_storeu_si128((__m128i*)(TmpBuf + 3 * 16), _mm_xor_si128(_mm_loadu_si128((const __m128i*)(TmpBuf + 3 * 16)), r0_3));
                                }
                                else
                                {
                                        _mm_storeu_si128((__m128i*)(TmpBuf + 0 * 16), r0_0);
                                        _mm_storeu_si128((__m128i*)(TmpBuf + 1 * 16), r0_1);
                                        _mm_storeu_si128((__m128i*)(TmpBuf + 2 * 16), r0_2);
                                        _mm_storeu_si128((__m128i*)(TmpBuf + 3 * 16), r0_3);
                                }
                                memcpy(CurrentOut, TmpBuf, RemainingBytes);
                                ChaCha20AddCounter(state, 1);
                                return;
                        }
                }
        }

    #else

        void ChaCha20EncryptBytes(uint8_t* state, uint8_t* In, uint8_t* Out, uint64_t Size, int rounds)
        {
                //portable chacha, no simd
                uint8_t* CurrentIn = In;
                uint8_t* CurrentOut = Out;
                uint64_t RemainingBytes = Size;
                uint32_t* state_dwords = (uint32_t*)state;
                uint32_t b[16];
                while (1)
                {
                        b[0] = ConstState[0];
                        b[1] = ConstState[1];
                        b[2] = ConstState[2];
                        b[3] = ConstState[3];
                        memcpy(((uint8_t*)b) + 16, state, 48);


                        for (int i = 0; i < rounds; i+= 2)
                        {
                                b[0] = b[0] + b[4];
                                b[12] = (b[12] ^ b[0]) << 16 | (b[12] ^ b[0]) >> 16;
                                b[8] = b[8] + b[12]; b[4] = (b[4] ^ b[8]) << 12 | (b[4] ^ b[8]) >> 20;
                                b[0] = b[0] + b[4];
                                b[12] = (b[12] ^ b[0]) << 8 | (b[12] ^ b[0]) >> 24;
                                b[8] = b[8] + b[12];
                                b[4] = (b[4] ^ b[8]) << 7 | (b[4] ^ b[8]) >> 25;
                                b[1] = b[1] + b[5];
                                b[13] = (b[13] ^ b[1]) << 16 | (b[13] ^ b[1]) >> 16;
                                b[9] = b[9] + b[13];
                                b[5] = (b[5] ^ b[9]) << 12 | (b[5] ^ b[9]) >> 20;
                                b[1] = b[1] + b[5];
                                b[13] = (b[13] ^ b[1]) << 8 | (b[13] ^ b[1]) >> 24;
                                b[9] = b[9] + b[13];
                                b[5] = (b[5] ^ b[9]) << 7 | (b[5] ^ b[9]) >> 25;
                                b[2] = b[2] + b[6];
                                b[14] = (b[14] ^ b[2]) << 16 | (b[14] ^ b[2]) >> 16;
                                b[10] = b[10] + b[14];
                                b[6] = (b[6] ^ b[10]) << 12 | (b[6] ^ b[10]) >> 20;
                                b[2] = b[2] + b[6];
                                b[14] = (b[14] ^ b[2]) << 8 | (b[14] ^ b[2]) >> 24;
                                b[10] = b[10] + b[14];
                                b[6] = (b[6] ^ b[10]) << 7 | (b[6] ^ b[10]) >> 25;
                                b[3] = b[3] + b[7];
                                b[15] = (b[15] ^ b[3]) << 16 | (b[15] ^ b[3]) >> 16;
                                b[11] = b[11] + b[15];
                                b[7] = (b[7] ^ b[11]) << 12 | (b[7] ^ b[11]) >> 20;
                                b[3] = b[3] + b[7];
                                b[15] = (b[15] ^ b[3]) << 8 | (b[15] ^ b[3]) >> 24;
                                b[11] = b[11] + b[15];
                                b[7] = (b[7] ^ b[11]) << 7 | (b[7] ^ b[11]) >> 25;
                                b[0] = b[0] + b[5];
                                b[15] = (b[15] ^ b[0]) << 16 | (b[15] ^ b[0]) >> 16;
                                b[10] = b[10] + b[15];
                                b[5] = (b[5] ^ b[10]) << 12 | (b[5] ^ b[10]) >> 20;
                                b[0] = b[0] + b[5];
                                b[15] = (b[15] ^ b[0]) << 8 | (b[15] ^ b[0]) >> 24;
                                b[10] = b[10] + b[15];
                                b[5] = (b[5] ^ b[10]) << 7 | (b[5] ^ b[10]) >> 25;
                                b[1] = b[1] + b[6];
                                b[12] = (b[12] ^ b[1]) << 16 | (b[12] ^ b[1]) >> 16;
                                b[11] = b[11] + b[12];
                                b[6] = (b[6] ^ b[11]) << 12 | (b[6] ^ b[11]) >> 20;
                                b[1] = b[1] + b[6];
                                b[12] = (b[12] ^ b[1]) << 8 | (b[12] ^ b[1]) >> 24;
                                b[11] = b[11] + b[12];
                                b[6] = (b[6] ^ b[11]) << 7 | (b[6] ^ b[11]) >> 25;
                                b[2] = b[2] + b[7];
                                b[13] = (b[13] ^ b[2]) << 16 | (b[13] ^ b[2]) >> 16;
                                b[8] = b[8] + b[13];
                                b[7] = (b[7] ^ b[8]) << 12 | (b[7] ^ b[8]) >> 20;
                                b[2] = b[2] + b[7];
                                b[13] = (b[13] ^ b[2]) << 8 | (b[13] ^ b[2]) >> 24;
                                b[8] = b[8] + b[13];
                                b[7] = (b[7] ^ b[8]) << 7 | (b[7] ^ b[8]) >> 25;
                                b[3] = b[3] + b[4];
                                b[14] = (b[14] ^ b[3]) << 16 | (b[14] ^ b[3]) >> 16;
                                b[9] = b[9] + b[14];
                                b[4] = (b[4] ^ b[9]) << 12 | (b[4] ^ b[9]) >> 20;
                                b[3] = b[3] + b[4];
                                b[14] = (b[14] ^ b[3]) << 8 | (b[14] ^ b[3]) >> 24;
                                b[9] = b[9] + b[14];
                                b[4] = (b[4] ^ b[9]) << 7 | (b[4] ^ b[9]) >> 25;
                        }

                        for (uint32_t i = 0; i < 4; ++i)
                        {
                                b[i] += ConstState[i];
                        }
                        for (uint32_t i = 0; i < 12; ++i)
                        {
                                b[i + 4] += state_dwords[i];
                        }

                        ++state_dwords[8]; //counter

                        if (RemainingBytes >= 64)
                        {
                                if (In)
                                {
                                        uint32_t* In32bits = (uint32_t*)CurrentIn;
                                        uint32_t* Out32bits = (uint32_t*)CurrentOut;
                                        for (uint32_t i = 0; i < 16; i++)
                                        {
                                                Out32bits[i] = In32bits[i] ^ b[i];
                                        }
                                }
                                else
                                        memcpy(CurrentOut, b, 64);

                                if (In) CurrentIn += 64;
                                CurrentOut += 64;
                                RemainingBytes -= 64;
                                if (RemainingBytes == 0) return;
                                continue;
                        }
                        else
                        {
                                if (In)
                                {
                                        for (int32_t i = 0; i < RemainingBytes; i++)
                                                CurrentOut[i] = CurrentIn[i] ^ ((uint8_t*)b)[i];
                                }
                                else memcpy(CurrentOut, b, RemainingBytes);
                                return;
                        }
                }
        }

    #endif

#endif