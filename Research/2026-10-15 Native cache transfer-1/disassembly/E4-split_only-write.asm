?write@@YAXAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@HH_N@Z PROC ; write, COMDAT

; 75   : {

$LN185:
	push	rbx
	push	rbp
	push	rsi
	push	rdi
	push	r14
	push	r15
	sub	rsp, 40					; 00000028H
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	mov	r10, QWORD PTR [rdx+8]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 75   : {

	mov	r14, rdx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	sub	r10, QWORD PTR [rdx]
	mov	rax, 3074457345618258603		; 2aaaaaaaaaaaaaabH
	imul	r10
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 77   :     if (partial) {

	xor	ebx, ebx
	mov	ebp, r9d
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	sar	rdx, 1
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 75   : {

	mov	esi, r8d
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	mov	r15, rdx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 75   : {

	mov	rdi, rcx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	shr	r15, 63					; 0000003fH
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 76   :     const size_t mask = coords.size() - 1;

	dec	r15
	add	r15, rdx

; 77   :     if (partial) {

	cmp	BYTE PTR partial$[rsp], bl
	je	$LN8@write

; 78   :         for (int i = 0; i < count; ++i) {

	test	r8d, r8d
	jle	$LN6@write
	npad	11
$LL4@write:

; 79   :             const size_t index = (size_t(i) << 1) & mask;

	mov	eax, ebx
	add	rax, rax
	and	rax, r15

; 80   :             a.setValueOn(coords[index], uint16_t(1 + ((index + phase) & 7)));

	lea	edx, DWORD PTR [rax+rbp]
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rcx, QWORD PTR [rax+rax*2]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 80   :             a.setValueOn(coords[index], uint16_t(1 + ((index + phase) & 7)));

	and	dx, 7
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	mov	rax, QWORD PTR [r14]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 80   :             a.setValueOn(coords[index], uint16_t(1 + ((index + phase) & 7)));

	inc	dx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	r9, QWORD PTR [rax+rcx*4]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 80   :             a.setValueOn(coords[index], uint16_t(1 + ((index + phase) & 7)));

	mov	WORD PTR $T1[rsp], dx
; File D:\source\openvdb\openvdb\openvdb\math\Coord.h

; 134  :     Int32 operator[](size_t i) const { OPENVDB_ASSERT(i < 3); return mVec[i]; }

	mov	r10d, DWORD PTR [r9]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 1032 :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, r10d
	and	eax, -8
	cmp	eax, DWORD PTR [rdi+24]
	jne	SHORT $LN20@write
	mov	ecx, DWORD PTR [r9+4]
	mov	eax, ecx
	and	eax, -8
	cmp	eax, DWORD PTR [rdi+28]
	jne	SHORT $LN20@write
	mov	r8d, DWORD PTR [r9+8]
	mov	eax, r8d
	and	eax, -8
	cmp	eax, DWORD PTR [rdi+32]
	jne	SHORT $LN20@write
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	and	r8d, 7
	and	ecx, 7
	and	r10d, 7
	lea	eax, DWORD PTR [rcx+r10*8]
	lea	r8d, DWORD PTR [r8+rax*8]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 588  :         const_cast<ValueType&>(LeafCacheT::buffer()[offset]) = value;

	mov	rax, QWORD PTR [rdi+16]
	mov	WORD PTR [rax+r8*2], dx
; File D:\source\openvdb\openvdb\openvdb\util\NodeMasks.h

; 455  :         mWords[n >> 6] |=  Word(1) << (n & 63);

	mov	edx, r8d
	mov	rcx, QWORD PTR [rdi+88]
	and	r8d, 63					; 0000003fH
	shr	rdx, 6
	mov	rax, QWORD PTR [rcx+rdx*8+16]
	bts	rax, r8
	mov	QWORD PTR [rcx+rdx*8+16], rax
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 580  :                 return;

	jmp	SHORT $LN2@write
$LN20@write:

; 581  :             }
; 582  :             this->setValueSecondary(xyz, value);

	lea	r8, QWORD PTR $T1[rsp]
	mov	rdx, r9
	mov	rcx, rdi
	call	?setValueSecondary@?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@34@AEBG@Z ; openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> >::setValueSecondary
$LN2@write:
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 78   :         for (int i = 0; i < count; ++i) {

	inc	ebx
	cmp	ebx, esi
	jl	$LL4@write

; 85   :     }
; 86   : }

	add	rsp, 40					; 00000028H
	pop	r15
	pop	r14
	pop	rdi
	pop	rsi
	pop	rbp
	pop	rbx
	ret	0
$LN8@write:

; 83   :         for (int i = 0; i < count; ++i)

	test	esi, esi
	jle	$LN6@write
	npad	3
$LL7@write:

; 84   :             a.setValueOn(coords[size_t(i) & mask], uint16_t(1 + ((i + phase) & 7)));

	mov	eax, ebx
	lea	edx, DWORD PTR [rbx+rbp]
	and	rax, r15
	and	dx, 7
	inc	dx
	mov	WORD PTR $T2[rsp], dx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rcx, QWORD PTR [rax+rax*2]
	mov	rax, QWORD PTR [r14]
; File D:\source\openvdb\openvdb\openvdb\math\Coord.h

; 134  :     Int32 operator[](size_t i) const { OPENVDB_ASSERT(i < 3); return mVec[i]; }

	mov	r10d, DWORD PTR [rax+rcx*4]
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	r9, QWORD PTR [rax+rcx*4]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 1032 :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, r10d
	and	eax, -8
	cmp	eax, DWORD PTR [rdi+24]
	jne	SHORT $LN94@write
	mov	ecx, DWORD PTR [r9+4]
	mov	eax, ecx
	and	eax, -8
	cmp	eax, DWORD PTR [rdi+28]
	jne	SHORT $LN94@write
	mov	r8d, DWORD PTR [r9+8]
	mov	eax, r8d
	and	eax, -8
	cmp	eax, DWORD PTR [rdi+32]
	jne	SHORT $LN94@write
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	and	r8d, 7
	and	ecx, 7
	and	r10d, 7
	lea	eax, DWORD PTR [rcx+r10*8]
	lea	r8d, DWORD PTR [r8+rax*8]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 588  :         const_cast<ValueType&>(LeafCacheT::buffer()[offset]) = value;

	mov	rax, QWORD PTR [rdi+16]
	mov	WORD PTR [rax+r8*2], dx
; File D:\source\openvdb\openvdb\openvdb\util\NodeMasks.h

; 455  :         mWords[n >> 6] |=  Word(1) << (n & 63);

	mov	edx, r8d
	mov	rcx, QWORD PTR [rdi+88]
	and	r8d, 63					; 0000003fH
	shr	rdx, 6
	mov	rax, QWORD PTR [rcx+rdx*8+16]
	bts	rax, r8
	mov	QWORD PTR [rcx+rdx*8+16], rax
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 580  :                 return;

	jmp	SHORT $LN5@write
$LN94@write:

; 581  :             }
; 582  :             this->setValueSecondary(xyz, value);

	lea	r8, QWORD PTR $T2[rsp]
	mov	rdx, r9
	mov	rcx, rdi
	call	?setValueSecondary@?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@34@AEBG@Z ; openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> >::setValueSecondary
$LN5@write:
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 83   :         for (int i = 0; i < count; ++i)

	inc	ebx
	cmp	ebx, esi
	jl	$LL7@write
$LN6@write:

; 85   :     }
; 86   : }

	add	rsp, 40					; 00000028H
	pop	r15
	pop	r14
	pop	rdi
	pop	rsi
	pop	rbp
	pop	rbx
	ret	0
?write@@YAXAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@HH_N@Z ENDP ; write
