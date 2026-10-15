?write@@YAXAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@HH_N@Z PROC ; write, COMDAT

; 74   : {

$LN455:
	push	rbx
	push	rbp
	push	rsi
	push	rdi
	push	r14
	push	r15
	sub	rsp, 40					; 00000028H
	mov	rbx, rcx
	mov	r14, rdx
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	mov	rcx, QWORD PTR [rdx+8]
	mov	rax, 3074457345618258603		; 2aaaaaaaaaaaaaabH
	sub	rcx, QWORD PTR [rdx]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 76   :     if (partial) {

	xor	edi, edi
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	imul	rcx
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 74   : {

	mov	ebp, r9d
	mov	esi, r8d
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1922 :         return static_cast<size_type>(_My_data._Mylast - _My_data._Myfirst);

	sar	rdx, 1
	mov	r15, rdx
	shr	r15, 63					; 0000003fH
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 75   :     const size_t mask = coords.size() - 1;

	dec	r15
	add	r15, rdx

; 76   :     if (partial) {

	cmp	BYTE PTR partial$[rsp], dil
	je	$LN8@write

; 77   :         for (int i = 0; i < count; ++i) {

	test	r8d, r8d
	jle	$LN6@write
	npad	10
$LL4@write:

; 78   :             const size_t index = (size_t(i) << 1) & mask;

	mov	eax, edi
	add	rax, rax
	and	rax, r15
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rcx, QWORD PTR [rax+rax*2]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 79   :             a.setValueOn(coords[index], uint16_t(1 + ((index + phase) & 7)));

	lea	r8d, DWORD PTR [rax+rbp]
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	mov	rax, QWORD PTR [r14]
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 79   :             a.setValueOn(coords[index], uint16_t(1 + ((index + phase) & 7)));

	and	r8w, 7
	inc	r8w
	mov	WORD PTR $T9[rsp], r8w
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rdx, QWORD PTR [rax+rcx*4]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	ecx, DWORD PTR [rax+rcx*4]
	mov	eax, ecx
	and	eax, -8
	cmp	eax, DWORD PTR [rbx+24]
	jne	SHORT $LN32@write
	mov	r9d, DWORD PTR [rdx+4]
	mov	eax, r9d
	and	eax, -8
	cmp	eax, DWORD PTR [rbx+28]
	jne	SHORT $LN32@write
	mov	r10d, DWORD PTR [rdx+8]
	mov	eax, r10d
	and	eax, -8
	cmp	eax, DWORD PTR [rbx+32]
	jne	SHORT $LN32@write
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	mov	r11, QWORD PTR [rbx+88]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	and	ecx, 7
	and	r9d, 7
	and	r10d, 7
	lea	eax, DWORD PTR [r9+rcx*8]
	lea	ecx, DWORD PTR [r10+rax*8]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 560  :                     const_cast<ValueType&>(LeafCacheT::buffer()[offset]) = value;

	mov	rax, QWORD PTR [rbx+16]

; 561  : #ifdef EXPERIMENT_DENSE_MASK
; 562  :                     // This experiment targets ordinary leaves with at least 64 voxels.
; 563  :                     // Smaller leaves retain their original mask API and behavior.
; 564  :                     if constexpr (NodeType::LOG2DIM >= 2) {
; 565  :                         if (node->getValueMask().template getWord<Index64>(offset >> 6) != ~Index64(0))

	mov	edx, ecx
	shr	rdx, 6
	mov	WORD PTR [rax+rcx*2], r8w
	mov	rax, QWORD PTR [r11+rdx*8+16]
	cmp	rax, -1
	je	$LN2@write
; File D:\source\openvdb\openvdb\openvdb\util\NodeMasks.h

; 455  :         mWords[n >> 6] |=  Word(1) << (n & 63);

	and	ecx, 63					; 0000003fH
	bts	rax, rcx
	mov	QWORD PTR [r11+rdx*8+16], rax
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	$LN2@write
$LN32@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, ecx
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rbx+36]
	jne	SHORT $LN107@write
	mov	eax, DWORD PTR [rdx+4]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rbx+40]
	jne	SHORT $LN107@write
	mov	eax, DWORD PTR [rdx+8]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rbx+44]
	jne	SHORT $LN107@write

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+80]
	lea	r8, QWORD PTR $T9[rsp]
	mov	r9, rbx
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	SHORT $LN2@write
$LN107@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	and	ecx, -4096				; fffffffffffff000H
	cmp	ecx, DWORD PTR [rbx+48]
	jne	SHORT $LN158@write
	mov	eax, DWORD PTR [rdx+4]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rbx+52]
	jne	SHORT $LN158@write
	mov	eax, DWORD PTR [rdx+8]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rbx+56]
	jne	SHORT $LN158@write

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+72]
	lea	r8, QWORD PTR $T9[rsp]
	mov	r9, rbx
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5>::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	SHORT $LN2@write
$LN158@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+64]
	lea	r8, QWORD PTR $T9[rsp]
	mov	r9, rbx
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> >::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
$LN2@write:
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 77   :         for (int i = 0; i < count; ++i) {

	inc	edi
	cmp	edi, esi
	jl	$LL4@write

; 84   :     }
; 85   : }

	add	rsp, 40					; 00000028H
	pop	r15
	pop	r14
	pop	rdi
	pop	rsi
	pop	rbp
	pop	rbx
	ret	0
$LN8@write:

; 82   :         for (int i = 0; i < count; ++i)

	test	esi, esi
	jle	$LN6@write
	npad	2
$LL7@write:

; 83   :             a.setValueOn(coords[size_t(i) & mask], uint16_t(1 + ((i + phase) & 7)));

	mov	eax, edi
	lea	r8d, DWORD PTR [rdi+rbp]
	and	rax, r15
	and	r8w, 7
	inc	r8w
	mov	WORD PTR $T10[rsp], r8w
; File C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.51.36231\include\vector

; 1952 :         return _My_data._Myfirst[_Pos];

	lea	rcx, QWORD PTR [rax+rax*2]
	mov	rax, QWORD PTR [r14]
	lea	rdx, QWORD PTR [rax+rcx*4]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	ecx, DWORD PTR [rax+rcx*4]
	mov	eax, ecx
	and	eax, -8
	cmp	eax, DWORD PTR [rbx+24]
	jne	SHORT $LN232@write
	mov	r9d, DWORD PTR [rdx+4]
	mov	eax, r9d
	and	eax, -8
	cmp	eax, DWORD PTR [rbx+28]
	jne	SHORT $LN232@write
	mov	r10d, DWORD PTR [rdx+8]
	mov	eax, r10d
	and	eax, -8
	cmp	eax, DWORD PTR [rbx+32]
	jne	SHORT $LN232@write
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	mov	r11, QWORD PTR [rbx+88]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\LeafNode.h

; 1072 :     return ((xyz[0] & (DIM-1u)) << 2*Log2Dim)

	and	ecx, 7
	and	r9d, 7
	and	r10d, 7
	lea	eax, DWORD PTR [r9+rcx*8]
	lea	ecx, DWORD PTR [r10+rax*8]
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 560  :                     const_cast<ValueType&>(LeafCacheT::buffer()[offset]) = value;

	mov	rax, QWORD PTR [rbx+16]

; 561  : #ifdef EXPERIMENT_DENSE_MASK
; 562  :                     // This experiment targets ordinary leaves with at least 64 voxels.
; 563  :                     // Smaller leaves retain their original mask API and behavior.
; 564  :                     if constexpr (NodeType::LOG2DIM >= 2) {
; 565  :                         if (node->getValueMask().template getWord<Index64>(offset >> 6) != ~Index64(0))

	mov	edx, ecx
	shr	rdx, 6
	mov	WORD PTR [rax+rcx*2], r8w
	mov	rax, QWORD PTR [r11+rdx*8+16]
	cmp	rax, -1
	je	$LN5@write
; File D:\source\openvdb\openvdb\openvdb\util\NodeMasks.h

; 455  :         mWords[n >> 6] |=  Word(1) << (n & 63);

	and	ecx, 63					; 0000003fH
	bts	rax, rcx
	mov	QWORD PTR [r11+rdx*8+16], rax
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	$LN5@write
$LN232@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	mov	eax, ecx
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rbx+36]
	jne	SHORT $LN307@write
	mov	eax, DWORD PTR [rdx+4]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rbx+40]
	jne	SHORT $LN307@write
	mov	eax, DWORD PTR [rdx+8]
	and	eax, -128				; ffffffffffffff80H
	cmp	eax, DWORD PTR [rbx+44]
	jne	SHORT $LN307@write

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+80]
	lea	r8, QWORD PTR $T10[rsp]
	mov	r9, rbx
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	SHORT $LN5@write
$LN307@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 968  :             const bool matches = (xyz[0] & ~Coord::ValueType(NodeT::DIM-1)) == mKeys[Idx][0]

	and	ecx, -4096				; fffffffffffff000H
	cmp	ecx, DWORD PTR [rbx+48]
	jne	SHORT $LN358@write
	mov	eax, DWORD PTR [rdx+4]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rbx+52]
	jne	SHORT $LN358@write
	mov	eax, DWORD PTR [rdx+8]
	and	eax, -4096				; fffffffffffff000H
	cmp	eax, DWORD PTR [rbx+56]
	jne	SHORT $LN358@write

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+72]
	lea	r8, QWORD PTR $T10[rsp]
	mov	r9, rbx
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5>::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
; File D:\source\openvdb\openvdb\openvdb\TypeList.h

; 531  :         if (pred(Idx)) op(std::get<Idx>(tup));

	jmp	SHORT $LN5@write
$LN358@write:
; File D:\Workspaces\AI\openvdb-native-cache-transfer-20261015\overlay\openvdb\tree\ValueAccessor.h

; 575  :                     const_cast<NodeType*>(node)->setValueAndCache(xyz, value, *this);

	mov	rcx, QWORD PTR [rbx+64]
	lea	r8, QWORD PTR $T10[rsp]
	mov	r9, rbx
	call	??$setValueAndCache@V?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@@?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@QEAAXAEBVCoord@math@23@AEBGAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@123@@Z ; openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> >::setValueAndCache<openvdb::v13_0::tree::ValueAccessorImpl<openvdb::v13_0::tree::Tree<openvdb::v13_0::tree::RootNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::InternalNode<openvdb::v13_0::tree::LeafNode<unsigned short,3>,4>,5> > >,1,void,openvdb::v13_0::index_sequence<0,1,2> > >
$LN5@write:
; File D:\SourceCode\Tedd.OpenVDB\benchmarks\NativeCacheTransfer\main.cpp

; 82   :         for (int i = 0; i < count; ++i)

	inc	edi
	cmp	edi, esi
	jl	$LL7@write
$LN6@write:

; 84   :     }
; 85   : }

	add	rsp, 40					; 00000028H
	pop	r15
	pop	r14
	pop	rdi
	pop	rsi
	pop	rbp
	pop	rbx
	ret	0
?write@@YAXAEAV?$ValueAccessorImpl@V?$Tree@V?$RootNode@V?$InternalNode@V?$InternalNode@V?$LeafNode@G$02@tree@v13_0@openvdb@@$03@tree@v13_0@openvdb@@$04@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@@tree@v13_0@openvdb@@$00XU?$index_sequence@$0A@$00$01@34@@tree@v13_0@openvdb@@AEBV?$vector@VCoord@math@v13_0@openvdb@@V?$allocator@VCoord@math@v13_0@openvdb@@@std@@@std@@HH_N@Z ENDP ; write
