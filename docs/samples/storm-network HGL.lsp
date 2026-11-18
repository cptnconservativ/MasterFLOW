;;; MasterFlow hydraulic results for Civil 3D
;;; Network: Storm A
;;; Units: elevations ft, flow cfs (the drawing must use the same units; nothing is converted)
;;; Criteria: City of Rapid City Infrastructure Design Criteria Manual (Jan. 2012) Sec.4.6
;;; Written 2026-11-18 18:39 by MasterFlow 0.11.0
;;;
;;; Use: open the drawing with the pipe network, APPLOAD this file, then type MFHGL.
;;; Each Civil 3D pipe whose name matches gets HGL up/down, EGL up/down and flow rate.
;;; The run is one undo step (U undoes it). Structures have no HGL property in Civil 3D;
;;; their results are in the CSV and LandXML files exported with this one.
(vl-load-com)
(setq mf:network "Storm A")
(setq mf:units "ft, cfs")
;; (name hglUp hglDown eglUp eglDown flow)
(setq mf:pipes '(
  ("P-1" 102.164725 100.814283 102.881335 101.644878 12.0)
  ("P-2" 100.545417 99.344266 101.022114 100.115075 16.0)
))
(setq mf:props '(
  ("HGL up" HGLUp HydraulicGradeLineUp)
  ("HGL down" HGLDown HydraulicGradeLineDown)
  ("EGL up" EGLUp EnergyGradeLineUp)
  ("EGL down" EGLDown EnergyGradeLineDown)
  ("Flow rate" FlowRate)
))
(defun mf:put (obj names val / done r)
  ;; First property in NAMES that the object has and accepts VAL; nil when none.
  (if val
    (foreach nm names
      (if (and (not done) (vlax-property-available-p obj nm T))
        (progn
          (setq r (vl-catch-all-apply 'vlax-put-property (list obj nm val)))
          (if (not (vl-catch-all-error-p r)) (setq done nm))))))
  done)
(defun mf:netname (obj / net)
  (if (vlax-property-available-p obj 'Network)
    (progn
      (setq net (vl-catch-all-apply 'vlax-get-property (list obj 'Network)))
      (if (and (not (vl-catch-all-error-p net)) (= (type net) 'VLA-OBJECT) (vlax-property-available-p net 'Name))
        (vlax-get-property net 'Name)))))
(defun mf:addname (lst x) (if (member x lst) lst (cons x lst)))
(defun c:MFHGL (/ doc ss i obj nm rec onlynet ans go updated found missing usedprops failedvals k got)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (setq ss (ssget "_X" '((0 . "AECC_PIPE"))) go T)
  (cond
    ((null ss) (princ "\nMasterFlow: no Civil 3D pipes in this drawing; nothing changed.") (setq go nil))
    (T
      (setq i 0)
      (repeat (sslength ss)
        (if (= (mf:netname (vlax-ename->vla-object (ssname ss i))) mf:network) (setq onlynet T))
        (setq i (1+ i)))
      (if (not onlynet)
        (progn
          (initget "Yes No")
          (setq ans (getkword (strcat "\nNo pipe network named \"" mf:network "\". Match pipe names in every network? [Yes/No] <No>: ")))
          (if (/= ans "Yes") (progn (princ "\nMasterFlow: nothing changed.") (setq go nil)))))))
  (if go
    (progn
      (vla-StartUndoMark doc)
      (setq i 0 updated 0 found '())
      (repeat (sslength ss)
        (setq obj (vlax-ename->vla-object (ssname ss i)))
        (setq nm (vl-catch-all-apply 'vlax-get-property (list obj 'Name)))
        (if (and (= (type nm) 'STR)
                 (or (not onlynet) (= (mf:netname obj) mf:network))
                 (setq rec (assoc nm mf:pipes)))
          (progn
            (setq found (mf:addname found nm) k 1 got nil)
            (foreach pr mf:props
              (if (setq got (mf:put obj (cdr pr) (nth k rec)))
                (setq usedprops (mf:addname usedprops (vl-symbol-name got)))
                (setq failedvals (mf:addname failedvals (car pr))))
              (setq k (1+ k)))
            (setq updated (1+ updated))))
        (setq i (1+ i)))
      (vla-EndUndoMark doc)
      (foreach rec mf:pipes (if (not (member (car rec) found)) (setq missing (cons (car rec) missing))))
      (princ (strcat "\nMasterFlow: " (itoa updated) " of " (itoa (length mf:pipes)) " pipes updated (" mf:units ")."))
      (if usedprops (princ (strcat "\n  Properties set: " (apply 'strcat (mapcar '(lambda (x) (strcat x " ")) (reverse usedprops))))))
      (if failedvals
        (progn
          (princ (strcat "\n  Could not set: " (apply 'strcat (mapcar '(lambda (x) (strcat x "; ")) (reverse failedvals)))))
          (princ "\n  To list a pipe's properties: (vlax-dump-object (vlax-ename->vla-object (car (entsel))))")))
      (if missing
        (princ (strcat "\n  Not found in the drawing: " (apply 'strcat (mapcar '(lambda (x) (strcat x " ")) (reverse missing))))))))
  (princ))
(princ "\nMasterFlow results loaded. Type MFHGL to write them to the pipe network.")
(princ)
